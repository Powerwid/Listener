using System.Text.Json;
using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Listener.Api.Email;
using Microsoft.EntityFrameworkCore;

namespace Listener.Api.Automatic;

public sealed record StoredNotification(Guid Id, DateTimeOffset FechaRegistro, string? Error, BcpEmailResult? Datos)
{
    public bool Persistido => true;
    public bool PagoVerificado => false;
}

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

    private static StoredNotification Map(CorreoProcesado row) => new(row.Id, row.FechaRegistro,
        row.ResultadoJson is null ? row.CodigoMotivo : null,
        row.ResultadoJson is null ? null : JsonSerializer.Deserialize<BcpEmailResult>(row.ResultadoJson, BcpEmailCommand.JsonOptions));
}
