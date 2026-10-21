using Listener.Api.Bbva;
using Listener.Api.Devices;
using Listener.Api.Security;
using Microsoft.AspNetCore.Mvc;
using TonysGo.Listener.Bbva;

namespace Listener.Api.Controllers;

[ApiController]
[Route("api/v1/bbva")]
public sealed class BbvaController(BbvaNotificationService service, DevicePairingService devices, ILogger<BbvaController> logger) : ControllerBase
{
    [HttpGet("connection")]
    [ResponseCache(NoStore = true)]
    public async Task<IActionResult> Connection(CancellationToken ct)
    {
        try
        {
            var device = await AuthenticateAsync(ct);
            return device is null ? Unauthorized(new { error = "Vuelve a vincular el telefono escaneando un QR de Listener." })
                : Ok(new { conectado = true, servicio = "Listener", origen = "BBVA_EMPRESAS", fechaServidorUtc = DateTimeOffset.UtcNow });
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogError(exception, "No se pudo comprobar la vinculacion del telefono.");
            return StatusCode(503, new { error = "VINCULACION_NO_DISPONIBLE" });
        }
    }

    [HttpPost("notifications")]
    public async Task<IActionResult> Receive([FromBody] BbvaNotificationRequest input, CancellationToken ct)
    {
        try
        {
            var device = await AuthenticateAsync(ct);
            if (device is null) return Unauthorized(new { error = "TELEFONO_NO_VINCULADO" });
            if (!string.Equals(input.DeviceId, device.NotificationDeviceId, StringComparison.Ordinal))
                return StatusCode(403, new { error = "DISPOSITIVO_NO_CORRESPONDE" });
            return Ok(await service.IngestAsync(input, ct));
        }
        catch (BbvaInputException exception) { return BadRequest(new { error = exception.Code, message = exception.Message }); }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogError(exception, "No se pudo almacenar la notificacion BBVA.");
            return StatusCode(503, new { error = "LISTENER_NO_DISPONIBLE" });
        }
    }

    private Task<CollectorDevice?> AuthenticateAsync(CancellationToken ct) =>
        devices.AuthenticateAsync(Request.Headers[ApiKeyValidator.HeaderName].FirstOrDefault(), ct);
}