using System.Text.Json;
using Listener.Api.Devices;
using Listener.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QRCoder;
using TonysGo.Listener.Pairing;

namespace Listener.Api.Controllers;

[ApiController]
[Route("api/v1/admin/devices")]
[Authorize(AuthenticationSchemes = AdminCredentials.Scheme, Policy = AdminCredentials.Scheme)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true)]
public sealed class AdminDevicesController(DevicePairingService devices, IConfiguration configuration,
    IHostEnvironment environment, ILogger<AdminDevicesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        try { return Ok(await devices.ListAsync(ct)); }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogError(exception, "No se pudo consultar la lista de telefonos.");
            return StatusCode(503, new { error = "Aplica las migraciones de vinculacion y revisa la conexion SQL." });
        }
    }

    [HttpPost("pairings")]
    [EnableRateLimiting("admin-actions")]
    public async Task<IActionResult> CreateQr(CancellationToken ct)
    {
        var url = configuration["DevicePairing:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(url) && environment.IsDevelopment()) url = $"{Request.Scheme}://{Request.Host}";
        if (!DevicePairingProtocol.IsServerUrl(url, environment.IsDevelopment()))
            return StatusCode(503, new { error = "Configura DevicePairing:PublicBaseUrl con la URL HTTPS de Listener." });
        try
        {
            var qr = await devices.CreateAsync(url!, User.Identity!.Name!, ct);
            var json = JsonSerializer.Serialize(qr, DevicePairingJsonContext.Default.DevicePairingQr);
            var png = PngByteQRCodeHelper.GetQRCode(json, QRCodeGenerator.ECCLevel.Q, 8);
            return Ok(new { qrImage = "data:image/png;base64," + Convert.ToBase64String(png), expiresAt = qr.ExpiresAt });
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogError(exception, "No se pudo generar el QR de vinculacion.");
            return StatusCode(503, new { error = "No se pudo crear el QR. Revisa la base de datos y las migraciones de vinculacion." });
        }
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting("admin-actions")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) =>
        await devices.RevokeAsync(id, ct) ? NoContent() : NotFound();
}

[ApiController]
[Route("api/v1/devices/pair")]
[EnableRateLimiting("device-pairing")]
public sealed class DevicePairingController(DevicePairingService devices, ILogger<DevicePairingController> logger) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(4096)]
    [ResponseCache(NoStore = true)]
    public async Task<IActionResult> Pair([FromBody] DevicePairingRequest input, CancellationToken ct)
    {
        try { return Ok(await devices.RedeemAsync(input, ct)); }
        catch (DevicePairingException exception) { return BadRequest(new { error = exception.Message }); }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogError(exception, "No se pudo completar la vinculacion del telefono.");
            return StatusCode(503, new { error = "No se pudo vincular. Revisa la conexion y vuelve a generar el QR." });
        }
    }
}
