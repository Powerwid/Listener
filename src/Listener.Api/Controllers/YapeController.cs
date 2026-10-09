using Listener.Api.Security;
using Listener.Api.Yape;
using Microsoft.AspNetCore.Mvc;

namespace Listener.Api.Controllers;

[ApiController]
[Route("api/v1/yape")]
public sealed class YapeController(
    YapeVerificationService service,
    ApiKeyValidator apiKeys,
    ILogger<YapeController> logger) : ControllerBase
{
    [HttpGet("connection")]
    [ResponseCache(NoStore = true)]
    public IActionResult CheckCollectorConnection()
    {
        var denied = Authorize(ApiClient.AndroidCollector);
        if (denied is not null) return denied;

        return Ok(new
        {
            conectado = true,
            servicio = "Listener",
            origen = "AndroidCollector",
            fechaServidorUtc = DateTimeOffset.UtcNow
        });
    }

    [HttpPost("notifications")]
    public async Task<IActionResult> ReceiveNotification([FromBody] YapeNotificationRequest request, CancellationToken ct)
    {
        var eventId = Short(request.NotificationKey);
        logger.LogInformation(
            "[Yape intercept] Recepcion iniciada. Evento={EventId} Dispositivo={DeviceId} Paquete={PackageName}",
            eventId, request.DeviceId, request.PackageName);
        var denied = Authorize(ApiClient.AndroidCollector);
        if (denied is not null)
        {
            logger.LogWarning("[Yape intercept] Acceso rechazado. Evento={EventId}", eventId);
            return denied;
        }
        try
        {
            var result = await service.IngestAsync(request, ct);
            logger.LogInformation(
                "[Yape intercept] Procesamiento terminado. Evento={EventId} Duplicada={Duplicada} EsIngreso={EsIngreso} Monto={Monto} Motivo={Motivo} Resueltas={Resueltas}",
                eventId, result.Duplicada, result.EsIngreso, result.Monto, result.CodigoMotivo, result.SolicitudesResueltas);
            return Ok(result);
        }
        catch (YapeInputException ex)
        {
            logger.LogWarning("[Yape intercept] Entrada rechazada. Evento={EventId} Motivo={Motivo}", eventId, ex.Code);
            return BadRequest(new { error = ex.Code, message = ex.Message });
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogError(exception, "[Yape intercept] Error no controlado. Evento={EventId}", eventId);
            return StatusCode(503, new { error = "LISTENER_NO_DISPONIBLE" });
        }
    }

    [HttpPost("verifications")]
    public async Task<IActionResult> CreateVerification([FromBody] VerificationCreateRequest request, CancellationToken ct)
    {
        var denied = Authorize(ApiClient.TonysGo);
        if (denied is not null) return denied;
        try { return Ok(await service.CreateAsync(request, ct)); }
        catch (YapeInputException ex) when (ex.Code == "REFERENCIA_EN_CONFLICTO")
        { return Conflict(new { error = ex.Code, message = ex.Message }); }
        catch (YapeInputException ex) { return BadRequest(new { error = ex.Code, message = ex.Message }); }
        catch (Exception) when (!ct.IsCancellationRequested)
        { return StatusCode(503, new { error = "LISTENER_NO_DISPONIBLE" }); }
    }

    [HttpGet("verifications/{reference}")]
    public async Task<IActionResult> GetVerification(string reference, CancellationToken ct)
    {
        var denied = Authorize(ApiClient.TonysGo);
        if (denied is not null) return denied;
        try
        {
            var result = await service.FindAsync(reference, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (YapeInputException ex) { return BadRequest(new { error = ex.Code, message = ex.Message }); }
        catch (Exception) when (!ct.IsCancellationRequested)
        { return StatusCode(503, new { error = "LISTENER_NO_DISPONIBLE" }); }
    }

    private IActionResult? Authorize(ApiClient client)
    {
        if (!apiKeys.IsConfigured(client))
            return StatusCode(503, new { error = "SEGURIDAD_NO_CONFIGURADA" });
        return apiKeys.IsValid(client, Request.Headers[ApiKeyValidator.HeaderName].FirstOrDefault())
            ? null
            : Unauthorized(new { error = "API_KEY_INVALIDA" });
    }

    private static string Short(string? value) => string.IsNullOrWhiteSpace(value)
        ? "sin-id" : value.Length <= 12 ? value : value[..12];
}
