using Listener.Api.Automatic;
using Microsoft.AspNetCore.Mvc;

namespace Listener.Api.Controllers;

// Development/local access is enforced by the middleware, including when the
// controllers are discovered in Production. No external TonysGo client yet.
[ApiController]
[Route("api/notificaciones")]
public sealed class NotificationsController(NotificationQueries queries) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        try { return Ok(new { limite = 100, resultados = await queries.LatestAsync(ct) }); }
        catch (Exception) when (!ct.IsCancellationRequested)
        { return StatusCode(503, new { error = "RESULTADOS_NO_DISPONIBLES" }); }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await queries.FindAsync(id, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        { return StatusCode(503, new { error = "RESULTADOS_NO_DISPONIBLES" }); }
    }
}
