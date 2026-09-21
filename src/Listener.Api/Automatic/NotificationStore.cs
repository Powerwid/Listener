using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Listener.Api.Email;
using Microsoft.EntityFrameworkCore;

namespace Listener.Api.Automatic;

public sealed class NotificationStore(IServiceScopeFactory scopes)
{
    public async Task<MailCheckpoint> GetCheckpointAsync(string email, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
        var cursor = await db.EstadoSincronizacionCorreo.AsNoTracking().SingleOrDefaultAsync(x => x.Buzon == email && x.Carpeta == "INBOX", ct);
        return new(cursor?.UidValidity, cursor?.UltimoUidProcesado ?? 0);
    }

    public async Task<int> SaveAsync(string email, MailCheckpoint expected, MailPoll poll, CancellationToken ct)
    {
        var parsed = new List<(IncomingMail Mail, BcpEmailResult? Result)>();
        foreach (var mail in poll.Messages)
        {
            BcpEmailResult? result = null;
            if (mail.Raw is not null)
            {
                try
                {
                    using var stream = new MemoryStream(mail.Raw);
                    result = await BcpEmailCommand.InspectAsync(stream, ct);
                }
                catch (FormatException) { }
                catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { }
            }
            parsed.Add((mail, result));
        }
        using var strategyScope = scopes.CreateScope();
        var strategyDb = strategyScope.ServiceProvider.GetRequiredService<ListenerDbContext>();
        return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var cursor = await db.EstadoSincronizacionCorreo.SingleOrDefaultAsync(x => x.Buzon == email && x.Carpeta == "INBOX", ct);
            if (cursor is not null && cursor.UidValidity == poll.Epoch && cursor.UltimoUidProcesado >= poll.LastUid)
                return 0; // Includes retry after an uncertain commit.
            if ((cursor?.UidValidity) != expected.Epoch || (cursor?.UltimoUidProcesado ?? 0) != expected.LastUid)
                throw new DbUpdateConcurrencyException("El cursor cambió durante la consulta.");
            if (cursor is null)
            {
                cursor = new() { Buzon = email, Carpeta = "INBOX" };
                db.EstadoSincronizacionCorreo.Add(cursor);
            }
            var saved = 0;
            foreach (var (mail, result) in parsed)
            {
                var reason = mail.Error ?? result?.Motivo ?? "FORMATO_NO_VALIDO";
                if (mail.Raw is null)
                {
                    db.EventosAuditoria.Add(new() { TipoEvento = "CorreoOmitido", Actor = "LectorAutomatico", CodigoMotivo = reason,
                        IdCorrelacion = $"{cursor.Id}:{poll.Epoch}:{mail.Uid}" });
                    continue;
                }
                var stored = new CorreoProcesado
                {
                    EstadoSincronizacionCorreo = cursor, UidValidity = poll.Epoch, Uid = mail.Uid,
                    HashContenido = Convert.ToHexString(SHA256.HashData(mail.Raw)), FechaRecepcion = mail.ReceivedAt,
                    MessageId = Limit(result?.MessageId, 998), Remitente = Limit(result?.Remitente, 320), Asunto = Limit(result?.Asunto, 1000),
                    VersionExtractor = "bcp-auto-v1", CodigoMotivo = reason,
                    EstadoProcesamiento = result is null ? EstadoProcesamientoCorreo.Error : result.Estado == "Descartado" ? EstadoProcesamientoCorreo.Descartado : EstadoProcesamientoCorreo.Procesado,
                    ResultadoJson = result is null ? null : JsonSerializer.Serialize(result, BcpEmailCommand.JsonOptions)
                };
                if (stored.ResultadoJson?.Length > 16000)
                {
                    stored.ResultadoJson = null;
                    stored.EstadoProcesamiento = EstadoProcesamientoCorreo.Error;
                    stored.CodigoMotivo = "RESULTADO_DEMASIADO_GRANDE";
                }
                db.CorreosProcesados.Add(stored);
                if (result?.Direccion == "Ingreso" && stored.ResultadoJson is not null)
                    db.EvidenciasPago.Add(new()
                    {
                        CorreoProcesado = stored, ProveedorPago = "YAPE", Monto = result.Monto, Moneda = result.Moneda,
                        FechaOperacion = result.FechaOperacion, NumeroOperacion = result.NumeroOperacion,
                        ReferenciaPagador = result.Ordenante, DireccionMovimiento = DireccionMovimiento.Ingreso,
                        EstadoValidacion = EstadoEvidencia.RequiereRevision, CodigoMotivo = reason
                    });
                db.EventosAuditoria.Add(new() { CorreoProcesado = stored, TipoEvento = "CorreoRecibidoAutomaticamente", Actor = "LectorAutomatico",
                    CodigoMotivo = stored.CodigoMotivo!, IdCorrelacion = stored.Id.ToString() });
                saved++;
            }
            cursor.UidValidity = poll.Epoch;
            cursor.UltimoUidProcesado = poll.LastUid;
            cursor.UltimaSincronizacionCorrecta = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return saved;
        });
    }

    private static string? Limit(string? value, int length) => value?.Length > length ? value[..length] : value;
}
