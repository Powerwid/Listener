using System.Net.Http.Json;
using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Listener.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace Listener.Api.Yape;

public sealed record TonysGoApproval(
    Guid VerificationId,
    string ReferenciaExterna,
    string ReferenciaConductor,
    decimal Monto,
    string Moneda,
    string CodigoSeguridad,
    Guid EvidenciaId,
    DateTimeOffset VerificadaEn);

public sealed class TonysGoCallbackDispatcher(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<TonysGoCallbackDispatcher> logger)
{
    public async Task<int> DispatchPendingAsync(CancellationToken ct)
    {
        var callbackUrl = configuration["TonysGo:CallbackUrl"];
        var apiKey = configuration["TonysGo:CallbackApiKey"];
        if (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out var uri)
            || !IsAllowedEndpoint(uri) || apiKey is not { Length: >= 32 })
            return 0;

        using var readScope = scopes.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<ListenerDbContext>();
        var pendingIds = await readDb.SolicitudesVerificacion.AsNoTracking()
            .Where(x => !x.EsSimulacion && x.Estado == EstadoSolicitud.Verificada
                && x.EvidenciaAsignada != null
                && !readDb.EventosAuditoria.Any(a => a.SolicitudVerificacionId == x.Id
                    && a.TipoEvento == "AprobacionEnviadaATonysGo"))
            .OrderBy(x => x.FechaResolucion).Select(x => x.Id).Take(50).ToListAsync(ct);

        var delivered = 0;
        foreach (var id in pendingIds)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
            var request = await db.SolicitudesVerificacion.Include(x => x.EvidenciaAsignada)
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            if (request?.EvidenciaAsignada is null || request.Estado != EstadoSolicitud.Verificada)
                continue;
            if (await db.EventosAuditoria.AnyAsync(x => x.SolicitudVerificacionId == id
                && x.TipoEvento == "AprobacionEnviadaATonysGo", ct)) continue;

            var payload = new TonysGoApproval(request.Id, request.ReferenciaExterna,
                request.ReferenciaConductor, request.MontoEsperado, request.Moneda,
                request.OperacionDeclarada!, request.EvidenciaAsignada.Id,
                request.FechaResolucion ?? DateTimeOffset.UtcNow);
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = JsonContent.Create(payload)
                };
                message.Headers.TryAddWithoutValidation(ApiKeyValidator.HeaderName, apiKey);
                using var response = await httpClients.CreateClient("TonysGoCallback").SendAsync(message, ct);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Tony's Go rechazo la aprobacion {VerificationId} con HTTP {StatusCode}.", id, (int)response.StatusCode);
                    continue;
                }
                db.EventosAuditoria.Add(new EventoAuditoria
                {
                    SolicitudVerificacionId = request.Id,
                    CorreoProcesadoId = request.EvidenciaAsignada.CorreoProcesadoId,
                    TipoEvento = "AprobacionEnviadaATonysGo",
                    Actor = "CallbackWorker",
                    EstadoAnterior = EstadoSolicitud.Verificada.ToString(),
                    EstadoNuevo = EstadoSolicitud.Verificada.ToString(),
                    CodigoMotivo = "HTTP_2XX",
                    IdCorrelacion = request.ReferenciaExterna
                });
                await db.SaveChangesAsync(ct);
                delivered++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "No se pudo entregar la aprobacion {VerificationId}; se reintentara.", id);
            }
        }
        return delivered;
    }

    private bool IsAllowedEndpoint(Uri uri) => uri.Scheme == Uri.UriSchemeHttps
        || environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
}

public sealed class TonysGoCallbackWorker(TonysGoCallbackDispatcher dispatcher) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try { await dispatcher.DispatchPendingAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { /* The dispatcher logs per-delivery failures; the next cycle retries. */ }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
