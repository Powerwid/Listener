using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using TonysGo.Listener.Bbva;

namespace Listener.Api.Bbva;

public sealed record StoredBbvaNotification(BbvaNotificationRequest Notification, BbvaPaymentData Payment);
// Original receipt fields remain compatible with the existing Android collector.
public sealed record BbvaNotificationReceipt(Guid Id, bool Duplicada, bool EsAbono, string? NombreCompleto,
    decimal? Monto, string CodigoMotivo, string EstadoEnvio, BbvaValidationResult? Validacion);
public sealed class BbvaInputException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class BbvaNotificationService(ListenerDbContext db, BbvaAbonoSender sender)
{
    // Stable ingestion identity across parser changes and notification retries.
    public const string Extractor = "android-bbva-v1";
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BbvaNotificationReceipt> IngestAsync(BbvaNotificationRequest input, CancellationToken ct)
    {
        Validate(input);
        input = input with { NotificationKey = input.NotificationKey.Trim(), DeviceId = input.DeviceId.Trim() };
        var payment = BbvaNotificationParser.Parse(input);
        var canonical = JsonSerializer.Serialize(new StoredBbvaNotification(input, payment), JsonOptions);
        if (canonical.Length > 16000)
            throw new BbvaInputException("CONTENIDO_DEMASIADO_GRANDE", "El aviso excede el limite de almacenamiento.");

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var mailbox = "android-bbva:" + input.DeviceId;
            var cursor = await db.EstadoSincronizacionCorreo.SingleOrDefaultAsync(
                x => x.Buzon == mailbox && x.Carpeta == "NOTIFICATIONS", ct);
            if (cursor is null)
            {
                cursor = new EstadoSincronizacionCorreo { Buzon = mailbox, Carpeta = "NOTIFICATIONS", UidValidity = 1 };
                db.EstadoSincronizacionCorreo.Add(cursor);
            }
            var existing = await db.CorreosProcesados.SingleOrDefaultAsync(x =>
                x.EstadoSincronizacionCorreoId == cursor.Id && x.MessageId == input.NotificationKey && x.VersionExtractor == Extractor, ct);
            if (existing is not null)
            {
                var saved = JsonSerializer.Deserialize<StoredBbvaNotification>(existing.ResultadoJson!, JsonOptions)!;
                var sent = await db.EventosAuditoria.AnyAsync(x => x.CorreoProcesadoId == existing.Id
                    && (x.TipoEvento == BbvaAbonoDispatcher.DeliveredEvent || x.TipoEvento == BbvaAbonoDispatcher.ValidationDeliveredEvent), ct);
                var validation = await db.BbvaPaymentValidations.FindAsync([existing.Id], ct);
                if (validation is null && !sent)
                {
                    validation = await BbvaPaymentLedger.ApplyAsync(db, existing, saved.Payment, ct);
                    await db.SaveChangesAsync(ct);
                }
                await transaction.CommitAsync(ct);
                return Result(existing.Id, true, saved.Payment, sent, validation);
            }
            if (cursor.UltimoUidProcesado >= uint.MaxValue)
                throw new BbvaInputException("SECUENCIA_AGOTADA", "El dispositivo alcanzo el limite de eventos.");
            cursor.UltimoUidProcesado++;
            cursor.UltimaSincronizacionCorrecta = DateTimeOffset.UtcNow;
            var stored = new CorreoProcesado
            {
                EstadoSincronizacionCorreo = cursor, UidValidity = 1, Uid = cursor.UltimoUidProcesado,
                MessageId = input.NotificationKey, Remitente = input.PackageName, Asunto = input.Title,
                FechaRecepcion = input.PostedAt,
                HashContenido = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))),
                ResultadoAutenticidad = ResultadoAutenticidad.NoVerificada,
                EstadoProcesamiento = payment.EsAbono ? EstadoProcesamientoCorreo.Procesado : EstadoProcesamientoCorreo.Descartado,
                CodigoMotivo = payment.CodigoMotivo,
                VersionExtractor = Extractor, ResultadoJson = canonical
            };
            db.CorreosProcesados.Add(stored);
            var result = await BbvaPaymentLedger.ApplyAsync(db, stored, payment, ct);
            db.EventosAuditoria.Add(new EventoAuditoria
            {
                CorreoProcesado = stored, TipoEvento = "NotificacionBbvaRecibida",
                Actor = "android:" + input.DeviceId[..Math.Min(90, input.DeviceId.Length)],
                CodigoMotivo = stored.CodigoMotivo, IdCorrelacion = stored.Id.ToString("D")
            });
            // There is no verification request, conductor assignment or wallet approval here.
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result(stored.Id, false, payment, false, result);
        });
    }

    private BbvaNotificationReceipt Result(Guid id, bool duplicate, BbvaPaymentData payment, bool sent, BbvaPaymentValidation? validation)
    {
        var result = validation is null ? null : BbvaPaymentLedger.Result(validation);
        return new(id, duplicate, payment.EsAbono, payment.NombreCompleto, payment.Monto,
            result?.Codigo ?? payment.CodigoMotivo,
            !payment.EsAbono ? "NO_APLICA" : sent ? "ENVIADO_A_TONYSGO"
                : sender.IsConfigured ? "PENDIENTE_ENVIO" : "PENDIENTE_CONFIGURACION", result);
    }

    public static void Validate(BbvaNotificationRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.NotificationKey) || input.NotificationKey.Length > 200
            || string.IsNullOrWhiteSpace(input.DeviceId) || input.DeviceId.Length > 100)
            throw new BbvaInputException("NOTIFICACION_INVALIDA", "Faltan identificadores o exceden el limite permitido.");
        if (input.PackageName != BbvaNotificationParser.OfficialPackage)
            throw new BbvaInputException("PAQUETE_NO_PERMITIDO", "Solo se admite BBVA Empresas.");
        // Queued notices retain their original time; the pending balance has no expiry.
        if (input.PostedAt <= DateTimeOffset.UnixEpoch || input.PostedAt > DateTimeOffset.UtcNow.AddMinutes(10))
            throw new BbvaInputException("FECHA_NOTIFICACION_INVALIDA", "Fecha de notificacion invalida.");
        if ((input.Title?.Length ?? 0) > 300 || (input.Text?.Length ?? 0) > 2000 || (input.BigText?.Length ?? 0) > 4000)
            throw new BbvaInputException("CONTENIDO_DEMASIADO_GRANDE", "El contenido excede el limite permitido.");
    }
}
