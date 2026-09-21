using System.Text;
using Listener.Api.Automatic;
using Listener.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Listener.Tests;

public class AutomaticReaderTests
{
    [Fact]
    public void StopCancelsTheSessionAndDoesNotReturnPasswordInStatus()
    {
        using var state = new AutomaticState();
        state.Start("sample@gmail.com", "fixture-only-password");
        var old = state.Credentials!;
        Assert.True(state.Status.Activo);
        Assert.DoesNotContain("fixture-only-password", System.Text.Json.JsonSerializer.Serialize(state.Status));
        state.Stop();
        Assert.True(old.Cancellation.IsCancellationRequested);
        Assert.Null(state.Credentials);
        Assert.False(state.Status.Activo);
        state.Complete(old, 5, false);
        Assert.Equal(0, state.Status.Guardados);
    }

    [Fact]
    public void ReconnectingCancelsOldWorkerAndIgnoresItsStatus()
    {
        using var state = new AutomaticState();
        state.Start("first@gmail.com", "fixture-password");
        var first = state.Credentials!;
        state.Start("second@gmail.com", "fixture-password");
        Assert.True(first.Cancellation.IsCancellationRequested);
        state.Fail(first, "Old error");
        Assert.Null(state.Status.Error);
        Assert.Equal("second@gmail.com", state.Status.Buzon);
    }

    [LocalSqlFact]
    public async Task AutomaticStoragePersistsResultAndCursorOnceEvenWhenBatchIsRetried()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ListenerDbContext>(options => options.UseSqlServer(
            "Server=.\\SQLEXPRESS;Database=ListenerDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5",
            sql => sql.EnableRetryOnFailure()));
        using var provider = services.BuildServiceProvider();
        var store = new NotificationStore(provider.GetRequiredService<IServiceScopeFactory>());
        var email = $"automatic-test-{Guid.NewGuid():N}@example.invalid";
        try
        {
            var initial = await store.GetCheckpointAsync(email, CancellationToken.None);
            Assert.Null(initial.Epoch);
            Assert.Equal(0, await store.SaveAsync(email, initial, new(1, 10, true, []), CancellationToken.None));
            var baseline = await store.GetCheckpointAsync(email, CancellationToken.None);
            Assert.Equal(10, baseline.LastUid);
            var raw = Encoding.UTF8.GetBytes("From: BCP <notificaciones@notificacionesbcp.com.pe>\r\nSubject: Constancia de recepción de Yapeo a celular BCP\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nRecibiste un yapeo de S/ 12.00 de Persona Prueba. Monto recibido S/ 12.00 Operación realizada Yapeo a celular Fecha y hora 18 de septiembre de 2026 - 12:05 PM Enviado por Persona Prueba ¿No reconoces esta operación?");
            var poll = new MailPoll(1, 11, false, [new(11, DateTimeOffset.UtcNow, raw, null)]);
            var counts = await Task.WhenAll(store.SaveAsync(email, baseline, poll, CancellationToken.None), store.SaveAsync(email, baseline, poll, CancellationToken.None));
            Assert.Equal(1, counts.Sum());
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
            var mail = await db.CorreosProcesados.Include(m => m.Evidencias).SingleAsync(m => m.EstadoSincronizacionCorreo.Buzon == email);
            Assert.False(mail.EsSimulacion);
            Assert.Contains("\"pagoVerificado\": false", mail.ResultadoJson!);
            Assert.Single(mail.Evidencias);
            Assert.Equal(12m, mail.Evidencias.Single().Monto);
            Assert.Null(mail.Evidencias.Single().SolicitudVerificacionId);
            Assert.Equal(11, (await store.GetCheckpointAsync(email, CancellationToken.None)).LastUid);
            var query = new NotificationQueries(db);
            Assert.Equal(mail.Id, (await query.FindAsync(mail.Id, CancellationToken.None))!.Id);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.SaveAsync(email, baseline,
                new(1, 12, false, [new(12, DateTimeOffset.UtcNow, raw, null)]), CancellationToken.None));
            Assert.Equal(11, (await store.GetCheckpointAsync(email, CancellationToken.None)).LastUid);
            // Run the actual hosted worker against a deterministic IMAP substitute.
            // It must consume a new notification and publish a persisted JSON result.
            using var state = new AutomaticState();
            state.Start(email, "fixture-password");
            using var worker = new AutomaticReaderWorker(state,
                new FixedMailbox(new(1, 12, false, [new(12, DateTimeOffset.UtcNow, raw, null)])), store);
            await worker.StartAsync(CancellationToken.None);
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (state.Status.Guardados == 0)
                    await Task.Delay(50, deadline.Token);
                Assert.Equal(1, state.Status.Guardados);
                Assert.Equal(12, (await store.GetCheckpointAsync(email, CancellationToken.None)).LastUid);
            }
            finally
            {
                state.Stop();
                await worker.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            // Exact test mailbox only; never remove application or user mail data.
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
            var ids = await db.CorreosProcesados.Where(x => x.EstadoSincronizacionCorreo.Buzon == email).Select(x => x.Id).ToListAsync();
            await db.EventosAuditoria.Where(x => x.CorreoProcesadoId != null && ids.Contains(x.CorreoProcesadoId.Value)).ExecuteDeleteAsync();
            await db.EvidenciasPago.Where(x => ids.Contains(x.CorreoProcesadoId)).ExecuteDeleteAsync();
            await db.CorreosProcesados.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
            await db.EstadoSincronizacionCorreo.Where(x => x.Buzon == email).ExecuteDeleteAsync();
        }
    }

    private sealed class FixedMailbox(MailPoll poll) : IAutomaticMailbox
    {
        public Task<MailPoll> PollAsync(MailboxCredentials credentials, MailCheckpoint checkpoint, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(poll);
        }
    }
}
