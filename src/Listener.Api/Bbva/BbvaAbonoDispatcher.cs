using System.Net.Http.Json;
using System.Text.Json;
using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Listener.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace Listener.Api.Bbva;

// EventoId is an idempotency key, never a bank operation number or a recharge approval.
public sealed record BbvaAbono(Guid EventoId, string NombreCompleto, decimal Monto, string Moneda,
    DateTimeOffset FechaNotificacion, string Origen = "BBVA_EMPRESAS");

public sealed class BbvaAbonoSender(IHttpClientFactory clients, IConfiguration configuration, IHostEnvironment environment)
{
    public bool IsConfigured => Endpoint() is not null && configuration["TonysGo:CallbackApiKey"] is { Length: >= 32 };

    private Uri? Endpoint() => Uri.TryCreate(configuration["TonysGo:AbonosUrl"], UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
            ? uri : null;

    public async Task<string> SendAsync(BbvaAbono abono, CancellationToken ct)
    {
        if (!IsConfigured) return "ENVIO_NO_CONFIGURADO";
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint());
        message.Headers.Add(ApiKeyValidator.HeaderName, configuration["TonysGo:CallbackApiKey"]);
        message.Content = JsonContent.Create(abono);
        using var response = await clients.CreateClient("TonysGoCallback").SendAsync(message, ct);
        return response.IsSuccessStatusCode ? "HTTP_2XX" : $"HTTP_{(int)response.StatusCode}";
    }
}

public sealed class BbvaAbonoDispatcher(IServiceScopeFactory scopes, BbvaAbonoSender sender, ILogger<BbvaAbonoDispatcher> logger)
{
    public const string DeliveredEvent = "AbonoBbvaEnviadoATonysGo";
    public const string FailedEvent = "EnvioAbonoBbvaFallido";

    public async Task<int> DispatchPendingAsync(CancellationToken ct)
    {
        if (!sender.IsConfigured) return 0;
        using var readScope = scopes.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<ListenerDbContext>();
        var retryAfter = DateTimeOffset.UtcNow.AddMinutes(-1);
        var ids = await readDb.CorreosProcesados.AsNoTracking().Where(x => !x.EsSimulacion
                && x.VersionExtractor == BbvaNotificationService.Extractor && x.CodigoMotivo == "ABONO_BBVA_EXTRAIDO"
                && !readDb.EventosAuditoria.Any(a => a.CorreoProcesadoId == x.Id && a.TipoEvento == DeliveredEvent)
                && !readDb.EventosAuditoria.Any(a => a.CorreoProcesadoId == x.Id && a.TipoEvento == FailedEvent && a.Fecha > retryAfter))
            .OrderBy(x => x.FechaRegistro).Select(x => x.Id).Take(50).ToListAsync(ct);
        var delivered = 0;
        foreach (var id in ids)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
            var row = await db.CorreosProcesados.SingleAsync(x => x.Id == id, ct);
            if (await db.EventosAuditoria.AnyAsync(x => x.CorreoProcesadoId == id && x.TipoEvento == DeliveredEvent, ct)) continue;
            string reason;
            try
            {
                var data = JsonSerializer.Deserialize<StoredBbvaNotification>(row.ResultadoJson!, BbvaNotificationService.JsonOptions);
                reason = data is { Payment: { EsAbono: true, NombreCompleto: string name, Monto: decimal amount } } && amount > 0
                    ? await sender.SendAsync(new BbvaAbono(row.Id, name, amount, "PEN", data.Notification.PostedAt), ct)
                    : "DATOS_GUARDADOS_INVALIDOS";
            }
            catch (Exception exception) when (exception is JsonException or HttpRequestException
                || exception is OperationCanceledException && !ct.IsCancellationRequested)
            {
                reason = exception is JsonException ? "DATOS_GUARDADOS_INVALIDOS" : "SIN_RESPUESTA_TONYSGO";
            }
            var success = reason == "HTTP_2XX";
            db.EventosAuditoria.Add(new EventoAuditoria
            {
                CorreoProcesadoId = id, TipoEvento = success ? DeliveredEvent : FailedEvent,
                Actor = "BbvaAbonoWorker", CodigoMotivo = reason, IdCorrelacion = id.ToString("D")
            });
            await db.SaveChangesAsync(ct);
            if (success) delivered++;
            else logger.LogWarning("Abono BBVA {EventoId} pendiente de entrega: {Motivo}.", id, reason);
        }
        return delivered;
    }
}

public sealed class BbvaAbonoWorker(BbvaAbonoDispatcher dispatcher, ILogger<BbvaAbonoWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try { await dispatcher.DispatchPendingAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "No se pudo procesar la cola de abonos BBVA."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
