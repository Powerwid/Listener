using Listener.Api.History;
using Listener.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Listener.Api.Controllers;

[ApiController]
[Route("api/v1/admin/history")]
[Authorize(AuthenticationSchemes = AdminCredentials.Scheme, Policy = AdminCredentials.Scheme)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true)]
public sealed class AdminHistoryController(NotificationHistory history, ILogger<AdminHistoryController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] HistoryFilter filter, CancellationToken ct)
    {
        try { return Ok(await history.ListAsync(filter, ct)); }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogError(exception, "No se pudo consultar el historial administrativo.");
            return StatusCode(503, new { error = "No se pudo consultar el historial. Revisa la conexión con ListenerDb y sus migraciones." });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        try { return await history.FindAsync(id, ct) is { } detail ? Ok(detail) : NotFound(); }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogError(exception, "No se pudo abrir el detalle {EventoId}.", id);
            return StatusCode(503, new { error = "No se pudo cargar el detalle del aviso." });
        }
    }

}
