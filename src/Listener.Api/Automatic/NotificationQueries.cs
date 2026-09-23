using System.Text.Json;
using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Listener.Api.Email;
using Listener.Api.Yape;
using Microsoft.EntityFrameworkCore;

namespace Listener.Api.Automatic;

public sealed record StoredNotification(Guid Id, DateTimeOffset FechaRegistro, string? Error, BcpEmailResult? Datos)
{
    public bool Persistido => true;
    public bool PagoVerificado => false;
}

public sealed record StoredAndroidYapeNotification(
    Guid Id,
    DateTimeOffset FechaRegistro,
    DateTimeOffset FechaNotificacion,
    string? Titulo,
    string Estado,
    string? CodigoMotivo,
    string? VersionExtractor,
    decimal? Monto,
    string? Pagador,
    string? CodigoSeguridad);

public sealed class NotificationQueries(ListenerDbContext db)
{
    private IQueryable<CorreoProcesado> Automatic => db.CorreosProcesados.AsNoTracking()
        .Where(c => !c.EsSimulacion && c.VersionExtractor == "bcp-auto-v1");

    public async Task<IReadOnlyList<StoredNotification>> LatestAsync(CancellationToken ct)
    {
        var rows = await Automatic.OrderByDescending(c => c.FechaRegistro).ThenBy(c => c.Id).Take(100).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<StoredNotification?> FindAsync(Guid id, CancellationToken ct)
    {
        var row = await Automatic.SingleOrDefaultAsync(c => c.Id == id, ct);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<StoredAndroidYapeNotification>> LatestAndroidYapeAsync(CancellationToken ct)
    {
        var rows = await db.CorreosProcesados.AsNoTracking()
            .Include(c => c.Evidencias)
            .Where(c => !c.EsSimulacion && c.VersionExtractor != null
                && c.VersionExtractor.StartsWith("android-yape-"))
            .OrderByDescending(c => c.FechaRegistro)
            .ThenBy(c => c.Id)
            .Take(100)
            .ToListAsync(ct);

        return rows.Select(MapAndroidYape).ToArray();
    }

    private static StoredNotification Map(CorreoProcesado row) => new(row.Id, row.FechaRegistro,
        row.ResultadoJson is null ? row.CodigoMotivo : null,
        row.ResultadoJson is null ? null : JsonSerializer.Deserialize<BcpEmailResult>(row.ResultadoJson, BcpEmailCommand.JsonOptions));

    private static StoredAndroidYapeNotification MapAndroidYape(CorreoProcesado row)
    {
        var evidence = row.Evidencias.OrderByDescending(e => e.FechaRegistro).FirstOrDefault();
        YapeNotificationData? parsed = null;
        if (!string.IsNullOrWhiteSpace(row.ResultadoJson))
        {
            try { parsed = JsonSerializer.Deserialize<YapeNotificationData>(row.ResultadoJson, BcpEmailCommand.JsonOptions); }
            catch (JsonException) { /* The stored reason still remains visible. */ }
        }

        return new StoredAndroidYapeNotification(
            row.Id,
            row.FechaRegistro,
            row.FechaRecepcion,
            row.Asunto,
            row.EstadoProcesamiento.ToString(),
            row.CodigoMotivo,
            row.VersionExtractor,
            evidence?.Monto ?? parsed?.Monto,
            evidence?.ReferenciaPagador ?? parsed?.Pagador,
            evidence?.ReferenciaSolicitudExtraida ?? parsed?.CodigoSeguridad);
    }
}
