using Listener.Api.History;
using Listener.Api.Reports;
using Listener.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Listener.Api.Controllers;

[ApiController]
[Route("api/v1/admin/reports")]
[Authorize(AuthenticationSchemes = AdminCredentials.Scheme, Policy = AdminCredentials.Scheme)]
[ResponseCache(NoStore = true)]
public sealed class AdminReportsController(NotificationHistory history, TimeProvider clock,
    ILogger<AdminReportsController> logger) : ControllerBase
{
    public const int ExportLimit = 50_000;

    [HttpGet]
    public async Task<IActionResult> Preview([FromQuery] HistoryFilter filter, CancellationToken ct)
    {
        filter.Source = "bbva";
        try { return Ok(await history.ListAsync(filter, ct)); }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogError(exception, "No se pudo consultar el reporte BBVA.");
            return StatusCode(503, new { error = "No se pudo consultar el reporte. Revisa la conexión y las migraciones de Listener." });
        }
    }

    [HttpGet("excel")]
    [EnableRateLimiting("admin-actions")]
    public async Task<IActionResult> Excel([FromQuery] HistoryFilter filter, CancellationToken ct)
    {
        filter.Source = "bbva";
        try
        {
            var rows = await history.ExportAsync(filter, ExportLimit, ct);
            if (rows.Count > ExportLimit)
                return UnprocessableEntity(new { error = "El reporte supera los 50 000 registros. Reduce el rango de fechas para exportarlo completo." });
            var generated = clock.GetUtcNow();
            var file = BbvaReportWorkbook.Create(rows, filter, generated);
            var name = $"Reporte_BBVA_{generated.ToOffset(TimeSpan.FromHours(-5)):yyyyMMdd_HHmmss}.xlsx";
            return File(file, BbvaReportWorkbook.ContentType, name);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogError(exception, "No se pudo exportar el reporte BBVA.");
            return StatusCode(503, new { error = "No se pudo generar el Excel. Revisa la conexión y vuelve a intentarlo." });
        }
    }
}
