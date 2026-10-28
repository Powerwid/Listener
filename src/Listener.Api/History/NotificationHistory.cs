using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Listener.Api.Bbva;
using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Listener.Api.Email;
using Listener.Api.Yape;
using Microsoft.EntityFrameworkCore;

namespace Listener.Api.History;

public sealed class HistoryFilter : IValidatableObject
{
    [StringLength(150)] public string? Search { get; set; }
    [RegularExpression("^(bbva|yape|correo)?$")] public string? Source { get; set; }
    [RegularExpression("^(pending|sent|failed|correct|insufficient|discarded|error)?$")] public string? Status { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    [Range(1, 1_000_000)] public int Page { get; set; } = 1;
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (From > To) yield return new("La fecha inicial no puede ser posterior a la final.", [nameof(From)]);
        if (To == DateOnly.MaxValue) yield return new("La fecha final está fuera del rango permitido.", [nameof(To)]);
    }
}

public sealed record HistoryItem(Guid Id, string Source, string? Name, decimal? Amount, string? Currency,
    string? Title, DateTimeOffset ReceivedAt, DateTimeOffset StoredAt, string? DeviceId,
    string Classification, string Status, string StatusLabel, string? Reason, BbvaValidationResult? Validation = null);
public sealed record HistorySummary(int Total, int BbvaPayments, int Pending, int Sent, int Discarded, int Insufficient = 0);
public sealed record HistoryPage(HistorySummary Summary, int Page, int PageSize, int Pages, IReadOnlyList<HistoryItem> Items);
public sealed record HistoryDetail(HistoryItem Item, string? Text, string? ExpandedText);

public sealed class NotificationHistory(ListenerDbContext db, BbvaAbonoSender sender)
{
    public const int PageSize = 25;
    private IQueryable<CorreoProcesado> Records => db.CorreosProcesados.AsNoTracking().Where(x => !x.EsSimulacion);
    private IQueryable<CorreoProcesado> Apply(HistoryFilter filter)
    {
        var rows = Records;
        rows = filter.Source switch
        {
            "bbva" => rows.Where(x => x.VersionExtractor == BbvaNotificationService.Extractor),
            "yape" => rows.Where(x => x.VersionExtractor != null && x.VersionExtractor.StartsWith("android-yape-")),
            "correo" => rows.Where(x => x.VersionExtractor == "bcp-auto-v1"),
            _ => rows
        };
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToUpperInvariant();
            // Earlier serializers escaped Spanish letters; searches must still find names such as José Muñoz.
            rows = rows.Where(x => (x.ResultadoJson != null && x.ResultadoJson.ToUpper()
                    .Replace("\\U00C1", "Á").Replace("\\U00E1", "Á").Replace("\\U00C9", "É").Replace("\\U00E9", "É")
                    .Replace("\\U00CD", "Í").Replace("\\U00ED", "Í").Replace("\\U00D3", "Ó").Replace("\\U00F3", "Ó")
                    .Replace("\\U00DA", "Ú").Replace("\\U00FA", "Ú").Replace("\\U00D1", "Ñ").Replace("\\U00F1", "Ñ")
                    .Replace("\\U00DC", "Ü").Replace("\\U00FC", "Ü").Contains(search))
                || (x.Asunto != null && x.Asunto.ToUpper().Contains(search))
                || (x.MessageId != null && x.MessageId.ToUpper().Contains(search))
                || (x.CodigoMotivo != null && x.CodigoMotivo.ToUpper().Contains(search)));
        }
        if (filter.From is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-5));
            rows = rows.Where(x => x.FechaRecepcion >= start);
        }
        if (filter.To is { } to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-5));
            rows = rows.Where(x => x.FechaRecepcion < end);
        }
        return filter.Status switch
        {
            "pending" => Payments(rows).Where(x => !db.EventosAuditoria.Any(a => a.CorreoProcesadoId == x.Id && (a.TipoEvento == BbvaAbonoDispatcher.DeliveredEvent || a.TipoEvento == BbvaAbonoDispatcher.ValidationDeliveredEvent))),
            "sent" => Payments(rows).Where(x => db.EventosAuditoria.Any(a => a.CorreoProcesadoId == x.Id && (a.TipoEvento == BbvaAbonoDispatcher.DeliveredEvent || a.TipoEvento == BbvaAbonoDispatcher.ValidationDeliveredEvent))),
            "failed" => Payments(rows).Where(x => !db.EventosAuditoria.Any(a => a.CorreoProcesadoId == x.Id && (a.TipoEvento == BbvaAbonoDispatcher.DeliveredEvent || a.TipoEvento == BbvaAbonoDispatcher.ValidationDeliveredEvent))
                && db.EventosAuditoria.Any(a => a.CorreoProcesadoId == x.Id && a.TipoEvento == BbvaAbonoDispatcher.FailedEvent)),
            "insufficient" => Payments(rows).Where(x => x.CodigoMotivo == BbvaPaymentLedger.InsufficientCode),
            "correct" => Payments(rows).Where(x => db.BbvaPaymentValidations.Any(v => v.EventId == x.Id && v.Correct)),
            "discarded" => rows.Where(x => x.EstadoProcesamiento == EstadoProcesamientoCorreo.Descartado),
            "error" => rows.Where(x => x.EstadoProcesamiento == EstadoProcesamientoCorreo.Error),
            _ => rows
        };
    }
    private static IQueryable<CorreoProcesado> Payments(IQueryable<CorreoProcesado> rows) => rows.Where(x =>
        x.VersionExtractor == BbvaNotificationService.Extractor && (x.CodigoMotivo == "ABONO_BBVA_EXTRAIDO" || x.CodigoMotivo == BbvaPaymentLedger.InsufficientCode)
        && x.EstadoProcesamiento == EstadoProcesamientoCorreo.Procesado);

    private sealed record Snapshot(CorreoProcesado Row, BbvaPaymentValidation? Validation, bool Sent, string? LastFailure);
    private IQueryable<Snapshot> Project(IQueryable<CorreoProcesado> rows) =>
        from row in rows
        join validation in db.BbvaPaymentValidations.AsNoTracking() on row.Id equals validation.EventId into validations
        from validation in validations.DefaultIfEmpty()
        select new Snapshot(row, validation,
            db.EventosAuditoria.Any(a => a.CorreoProcesadoId == row.Id && (a.TipoEvento == BbvaAbonoDispatcher.DeliveredEvent || a.TipoEvento == BbvaAbonoDispatcher.ValidationDeliveredEvent)),
            db.EventosAuditoria.Where(a => a.CorreoProcesadoId == row.Id && a.TipoEvento == BbvaAbonoDispatcher.FailedEvent)
                .OrderByDescending(a => a.Fecha).ThenBy(a => a.Id).Select(a => a.CodigoMotivo).FirstOrDefault());

    public async Task<HistoryPage> ListAsync(HistoryFilter filter, CancellationToken ct)
    {
        var rows = Apply(filter);
        var total = await rows.CountAsync(ct);
        var payments = Payments(rows);
        var bbva = await payments.CountAsync(ct);
        var sent = await payments.CountAsync(x => db.EventosAuditoria.Any(a => a.CorreoProcesadoId == x.Id && (a.TipoEvento == BbvaAbonoDispatcher.DeliveredEvent || a.TipoEvento == BbvaAbonoDispatcher.ValidationDeliveredEvent)), ct);
        var insufficient = await payments.CountAsync(x => x.CodigoMotivo == BbvaPaymentLedger.InsufficientCode, ct);
        var discarded = await rows.CountAsync(x => x.EstadoProcesamiento == EstadoProcesamientoCorreo.Descartado, ct);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        var page = Math.Clamp(filter.Page, 1, pages);
        var records = await Project(rows.OrderByDescending(x => x.FechaRecepcion).ThenBy(x => x.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)).ToListAsync(ct);
        return new(new(total, bbva, bbva - sent, sent, discarded, insufficient), page, PageSize, pages, records.Select(x => Map(x).Item).ToArray());
    }

    public async Task<HistoryDetail?> FindAsync(Guid id, CancellationToken ct)
    {
        var snapshot = await Project(Records.Where(x => x.Id == id)).SingleOrDefaultAsync(ct);
        if (snapshot is null) return null;
        var data = Map(snapshot);
        return new(data.Item, data.Text, data.BigText);
    }

    public async Task<IReadOnlyList<HistoryItem>> ExportAsync(HistoryFilter filter, int limit, CancellationToken ct)
    {
        var records = await Project(Apply(filter).OrderBy(x => x.FechaRecepcion).ThenBy(x => x.Id)
            .Take(limit + 1)).ToListAsync(ct);
        return records.Select(x => Map(x).Item).ToArray();
    }

    private (HistoryItem Item, string? Text, string? BigText) Map(Snapshot snapshot)
    {
        var row = snapshot.Row;
        var source = row.VersionExtractor == BbvaNotificationService.Extractor ? "BBVA Empresas"
            : row.VersionExtractor?.StartsWith("android-yape-", StringComparison.Ordinal) == true ? "Yape" : "Correo";
        string? name = null, currency = null, device = null, text = null, bigText = null;
        decimal? amount = null;
        var validPayment = false;
        var corrupt = false;
        try
        {
            if (row.ResultadoJson is { Length: > 0 } json)
            {
                if (source == "BBVA Empresas")
                {
                    var data = JsonSerializer.Deserialize<StoredBbvaNotification>(json, BbvaNotificationService.JsonOptions);
                    name = data?.Payment?.NombreCompleto; amount = data?.Payment?.Monto; currency = amount.HasValue ? "PEN" : null;
                    device = data?.Notification?.DeviceId; text = data?.Notification?.Text; bigText = data?.Notification?.BigText;
                    validPayment = data?.Payment is { EsAbono: true, NombreCompleto: { Length: > 0 }, Monto: > 0 }
                        && (row.CodigoMotivo == "ABONO_BBVA_EXTRAIDO" || row.CodigoMotivo == BbvaPaymentLedger.InsufficientCode) && row.EstadoProcesamiento == EstadoProcesamientoCorreo.Procesado;
                }
                else if (source == "Yape")
                {
                    var data = JsonSerializer.Deserialize<YapeNotificationData>(json, BcpEmailCommand.JsonOptions);
                    name = data?.Pagador; amount = data?.Monto; currency = data?.Moneda; device = data?.DeviceId;
                    text = data?.Text; bigText = data?.BigText;
                }
                else
                {
                    var data = JsonSerializer.Deserialize<BcpEmailResult>(json, BcpEmailCommand.JsonOptions);
                    name = data?.Ordenante ?? data?.Empresa; amount = data?.Monto; currency = data?.Moneda;
                }
            }
        }
        catch (JsonException) { corrupt = true; }
        var validation = snapshot.Validation is null ? null : BbvaPaymentLedger.Result(snapshot.Validation);
        var classification = row.EstadoProcesamiento switch
        {
            EstadoProcesamientoCorreo.Descartado => "Descartada",
            EstadoProcesamientoCorreo.Error => "Error de lectura",
            EstadoProcesamientoCorreo.Pendiente => "Por revisar",
            _ => validation is { Correcto: false } ? "Monto insuficiente" : validation is { Correcto: true } ? "Validación correcta" : validPayment ? "Abono detectado" : "Datos extraídos"
        };
        var status = "history";
        var label = "Historial";
        if (validPayment)
        {
            status = snapshot.Sent ? "sent" : snapshot.LastFailure is not null ? "failed" : "pending";
            label = snapshot.Sent ? validation is null ? "Entregado a Tony’s Go" : "Respuesta entregada a Tony’s Go" : !sender.IsConfigured ? "Envío sin configurar"
                : snapshot.LastFailure is not null ? "Envío con error" : "Pendiente de entrega";
        }
        if (corrupt || row.EstadoProcesamiento == EstadoProcesamientoCorreo.Error)
        { status = "error"; label = "Revisar lectura"; }
        return (new(row.Id, source, name, amount, currency, row.Asunto, row.FechaRecepcion, row.FechaRegistro, device,
            classification, status, label, corrupt ? "RESULTADO_GUARDADO_INVALIDO" : snapshot.Sent ? row.CodigoMotivo : snapshot.LastFailure ?? row.CodigoMotivo, validation), text, bigText);
    }
}
