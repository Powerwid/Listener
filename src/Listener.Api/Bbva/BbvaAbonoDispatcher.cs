using System.Data;
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

// Preserve the original abono fields and add accumulation and validation data.
public sealed record BbvaPaymentResponse(Guid EventoId, string NombreCompleto, decimal Monto, string Moneda,
    DateTimeOffset FechaNotificacion, string Origen, Guid AcumulacionId, long Secuencia,
    bool Correcto, decimal MontoAcumulado, decimal MontoFaltante);

public sealed class BbvaAbonoSender(IHttpClientFactory clients, IConfiguration configuration, IHostEnvironment environment)
{
    public bool IsConfigured => Endpoint() is not null && configuration["TonysGo:CallbackApiKey"] is { Length: >= 32 };

    private Uri? Endpoint() => Uri.TryCreate(configuration["TonysGo:AbonosUrl"], UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
            ? uri : null;

    public async Task<string> SendAsync(BbvaAbono abono, CancellationToken ct)
    {
        if (abono.Monto < BbvaPaymentLedger.Minimum) return BbvaPaymentLedger.InsufficientCode;
        if (!IsConfigured) return "ENVIO_NO_CONFIGURADO";
        return await PostAsync(abono, ct);
    }

    public async Task<string> SendValidationAsync(BbvaValidationResult result, CancellationToken ct)
    {
        if (result.MontoRecibido <= 0 || result.MontoAcumulado < result.MontoRecibido
            || result.Correcto != (result.MontoAcumulado >= BbvaPaymentLedger.Minimum)
            || result.MontoFaltante != Math.Max(0, BbvaPaymentLedger.Minimum - result.MontoAcumulado))
            return "VALIDACION_GUARDADA_INVALIDA";
        if (!IsConfigured) return "ENVIO_NO_CONFIGURADO";
        return await PostAsync(new BbvaPaymentResponse(result.EventoId, result.NombreCompleto,
            result.MontoRecibido, result.Moneda, result.FechaNotificacion, result.Origen,
            result.AcumulacionId, result.Secuencia, result.Correcto, result.MontoAcumulado, result.MontoFaltante), ct);
    }

    private async Task<string> PostAsync<T>(T payload, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint());
        message.Headers.Add(ApiKeyValidator.HeaderName, configuration["TonysGo:CallbackApiKey"]);
        message.Content = JsonContent.Create(payload);
        using var response = await clients.CreateClient("TonysGoCallback").SendAsync(message, ct);
        return response.IsSuccessStatusCode ? "HTTP_2XX" : $"HTTP_{(int)response.StatusCode}";
    }
}

public sealed class BbvaAbonoDispatcher(IServiceScopeFactory scopes, BbvaAbonoSender sender, ILogger<BbvaAbonoDispatcher> logger)
{
    public const string DeliveredEvent = "AbonoBbvaEnviadoATonysGo"; // historical deliveries
    public const string ValidationDeliveredEvent = "ValidacionBbvaEnviadaATonysGo";
    public const string FailedEvent = "EnvioAbonoBbvaFallido";

    public async Task<int> DispatchPendingAsync(CancellationToken ct)
    {
        if (!sender.IsConfigured) return 0;
        await PrepareLegacyAsync(ct);
        using var readScope = scopes.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<ListenerDbContext>();
        var retryAfter = DateTimeOffset.UtcNow.AddMinutes(-1);
        var ids = await readDb.BbvaPaymentValidations.AsNoTracking()
            .Where(v => !readDb.EventosAuditoria.Any(a => a.CorreoProcesadoId == v.EventId
                && (a.TipoEvento == DeliveredEvent || a.TipoEvento == ValidationDeliveredEvent)))
            .Where(v => !readDb.EventosAuditoria.Any(a => a.CorreoProcesadoId == v.EventId
                && a.TipoEvento == FailedEvent && a.Fecha > retryAfter))
            // A later result for this name must not overtake an earlier result awaiting acknowledgement.
            .Where(v => !readDb.BbvaPaymentValidations.Any(earlier => earlier.PayerKey == v.PayerKey && earlier.Sequence < v.Sequence
                && !readDb.EventosAuditoria.Any(a => a.CorreoProcesadoId == earlier.EventId
                    && (a.TipoEvento == DeliveredEvent || a.TipoEvento == ValidationDeliveredEvent))))
            .OrderBy(v => v.NotificationAt).ThenBy(v => v.EventId).Select(v => v.EventId).Take(50).ToListAsync(ct);
        var delivered = 0;
        foreach (var id in ids)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
            if (await db.EventosAuditoria.AnyAsync(x => x.CorreoProcesadoId == id
                && (x.TipoEvento == DeliveredEvent || x.TipoEvento == ValidationDeliveredEvent), ct)) continue;
            var validation = await db.BbvaPaymentValidations.SingleAsync(x => x.EventId == id, ct);
            string reason;
            try { reason = await sender.SendValidationAsync(BbvaPaymentLedger.Result(validation), ct); }
            catch (Exception exception) when (exception is HttpRequestException
                || exception is OperationCanceledException && !ct.IsCancellationRequested)
            { reason = "SIN_RESPUESTA_TONYSGO"; }
            var success = reason == "HTTP_2XX";
            db.EventosAuditoria.Add(new EventoAuditoria
            {
                CorreoProcesadoId = id, TipoEvento = success ? ValidationDeliveredEvent : FailedEvent,
                Actor = "BbvaAbonoWorker", CodigoMotivo = reason, IdCorrelacion = id.ToString("D")
            });
            await db.SaveChangesAsync(ct);
            if (success) delivered++;
            else logger.LogWarning("Resultado BBVA {EventoId} pendiente de entrega: {Motivo}.", id, reason);
        }
        return delivered;
    }

    private async Task PrepareLegacyAsync(CancellationToken ct)
    {
        using var read = scopes.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<ListenerDbContext>();
        var ids = await db.CorreosProcesados.AsNoTracking().Where(x => !x.EsSimulacion
            && x.VersionExtractor == BbvaNotificationService.Extractor && x.EstadoProcesamiento == EstadoProcesamientoCorreo.Procesado
            && (x.CodigoMotivo == "ABONO_BBVA_EXTRAIDO" || x.CodigoMotivo == BbvaPaymentLedger.InsufficientCode)
            && !db.BbvaPaymentValidations.Any(v => v.EventId == x.Id)
            && !db.EventosAuditoria.Any(a => a.CorreoProcesadoId == x.Id
                && (a.TipoEvento == DeliveredEvent || a.TipoEvento == ValidationDeliveredEvent)))
            .OrderBy(x => x.FechaRegistro).ThenBy(x => x.Id).Select(x => x.Id).Take(50).ToListAsync(ct);
        foreach (var id in ids)
        {
            using var scope = scopes.CreateScope();
            var write = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
            await write.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                write.ChangeTracker.Clear();
                await using var transaction = await write.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var row = await write.CorreosProcesados.SingleAsync(x => x.Id == id, ct);
                if (await write.EventosAuditoria.AnyAsync(a => a.CorreoProcesadoId == id
                    && (a.TipoEvento == DeliveredEvent || a.TipoEvento == ValidationDeliveredEvent), ct)) return;
                try
                {
                    var stored = JsonSerializer.Deserialize<StoredBbvaNotification>(row.ResultadoJson ?? "null", BbvaNotificationService.JsonOptions);
                    if (stored is not { Notification: not null, Payment: not null }
                        || await BbvaPaymentLedger.ApplyAsync(write, row, stored.Payment, ct) is null)
                    {
                        row.EstadoProcesamiento = EstadoProcesamientoCorreo.Error;
                        row.CodigoMotivo = "DATOS_GUARDADOS_INVALIDOS";
                    }
                    await write.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                }
                catch (JsonException)
                {
                    row.EstadoProcesamiento = EstadoProcesamientoCorreo.Error;
                    row.CodigoMotivo = "DATOS_GUARDADOS_INVALIDOS";
                    await write.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    logger.LogWarning("Resultado BBVA antiguo {EventoId} ilegible.", id);
                }
            });
        }
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
