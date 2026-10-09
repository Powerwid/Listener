using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Listener.Api.Email;
using Microsoft.EntityFrameworkCore;

namespace Listener.Api.Yape;

public sealed class YapeVerificationService(ListenerDbContext db, IConfiguration configuration)
{
    // Keep the ingest identity stable: replaying an old notification must not
    // create a second payment evidence just because parsing rules changed.
    private const string Extractor = "android-yape-v2";

    public async Task<NotificationIngestResult> IngestAsync(YapeNotificationRequest input, CancellationToken ct)
    {
        var parsed = YapeNotificationParser.Parse(input);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var mailbox = "android:" + parsed.DeviceId;
            var cursor = await db.EstadoSincronizacionCorreo.SingleOrDefaultAsync(
                x => x.Buzon == mailbox && x.Carpeta == "NOTIFICATIONS", ct);
            if (cursor is null)
            {
                cursor = new EstadoSincronizacionCorreo { Buzon = mailbox, Carpeta = "NOTIFICATIONS", UidValidity = 1 };
                db.EstadoSincronizacionCorreo.Add(cursor);
            }

            var existing = await db.CorreosProcesados.Include(x => x.Evidencias).SingleOrDefaultAsync(
                x => x.EstadoSincronizacionCorreoId == cursor.Id && x.MessageId == parsed.NotificationKey && x.VersionExtractor == Extractor, ct);
            if (existing is not null)
            {
                await transaction.CommitAsync(ct);
                var existingEvidence = existing.Evidencias.SingleOrDefault();
                return new NotificationIngestResult(existing.Id, true, existing.Remitente == YapeNotificationParser.OfficialPackage,
                    existingEvidence?.DireccionMovimiento == DireccionMovimiento.Ingreso, existingEvidence?.Monto,
                    existingEvidence?.ReferenciaSolicitudExtraida, existing.CodigoMotivo ?? "NOTIFICACION_DUPLICADA", 0);
            }

            if (cursor.UltimoUidProcesado >= uint.MaxValue)
                throw new YapeInputException("SECUENCIA_AGOTADA", "El dispositivo alcanzo el limite de eventos permitido.");
            cursor.UltimoUidProcesado++;
            cursor.UltimaSincronizacionCorrecta = DateTimeOffset.UtcNow;
            var canonical = JsonSerializer.Serialize(parsed, BcpEmailCommand.JsonOptions);
            var official = parsed.PackageName == YapeNotificationParser.OfficialPackage;
            var stored = new CorreoProcesado
            {
                EstadoSincronizacionCorreo = cursor,
                UidValidity = 1,
                Uid = cursor.UltimoUidProcesado,
                MessageId = parsed.NotificationKey,
                Remitente = parsed.PackageName,
                Asunto = parsed.Title,
                FechaRecepcion = parsed.PostedAt,
                HashContenido = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))),
                ResultadoAutenticidad = ResultadoAutenticidad.NoVerificada,
                EstadoProcesamiento = official && parsed.EsIngreso ? EstadoProcesamientoCorreo.Procesado : EstadoProcesamientoCorreo.Descartado,
                CodigoMotivo = parsed.CodigoMotivo,
                VersionExtractor = Extractor,
                ResultadoJson = canonical.Length <= 16000 ? canonical : null
            };
            db.CorreosProcesados.Add(stored);

            EvidenciaPago? evidence = null;
            if (official && parsed.EsIngreso)
            {
                evidence = new EvidenciaPago
                {
                    CorreoProcesado = stored,
                    ProveedorPago = "YAPE",
                    // Security codes repeat. Keep the code in the extracted-reference
                    // field and give the evidence a unique internal operation identity.
                    ReferenciaSolicitudExtraida = parsed.CodigoSeguridad,
                    NumeroOperacion = $"ANDROID-{stored.Id:N}",
                    ReferenciaCuentaReceptora = Clean(configuration["Yape:CuentaReceptora"], 120),
                    Monto = parsed.Monto,
                    Moneda = parsed.Moneda,
                    FechaOperacion = parsed.PostedAt,
                    ReferenciaPagador = Clean(parsed.Pagador, 200),
                    DireccionMovimiento = DireccionMovimiento.Ingreso,
                    EstadoPagoInformado = EstadoPagoInformado.Completado,
                    EstadoValidacion = EstadoEvidencia.RequiereRevision,
                    CodigoMotivo = parsed.CodigoMotivo
                };
                db.EvidenciasPago.Add(evidence);
            }
            db.EventosAuditoria.Add(new EventoAuditoria
            {
                CorreoProcesado = stored,
                TipoEvento = "NotificacionAndroidRecibida",
                Actor = "android:" + parsed.DeviceId,
                CodigoMotivo = parsed.CodigoMotivo,
                IdCorrelacion = parsed.NotificationKey
            });
            await db.SaveChangesAsync(ct);

            var resolved = 0;
            if (evidence?.ReferenciaSolicitudExtraida is not null)
            {
                var requests = await db.SolicitudesVerificacion.Where(x => !x.EsSimulacion
                    && x.OperacionDeclarada == evidence.ReferenciaSolicitudExtraida
                    && (x.Estado == EstadoSolicitud.Pendiente || x.Estado == EstadoSolicitud.RequiereRevision)).ToListAsync(ct);
                foreach (var request in requests)
                    if (await TryResolveAsync(request, ct)) resolved++;
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return new NotificationIngestResult(stored.Id, false, official, parsed.EsIngreso, parsed.Monto,
                parsed.CodigoSeguridad, parsed.CodigoMotivo, resolved);
        });
    }

    public async Task<VerificationResult> CreateAsync(VerificationCreateRequest input, CancellationToken ct)
    {
        Validate(input);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var existing = await db.SolicitudesVerificacion.Include(x => x.EvidenciaAsignada)
                .SingleOrDefaultAsync(x => x.ReferenciaExterna == input.ReferenciaExterna, ct);
            if (existing is not null)
            {
                if (!Same(existing, input))
                    throw new YapeInputException("REFERENCIA_EN_CONFLICTO", "La referencia ya existe con otros datos.");
                await transaction.CommitAsync(ct);
                return Map(existing);
            }

            var now = DateTimeOffset.UtcNow;
            var request = new SolicitudVerificacion
            {
                ReferenciaExterna = input.ReferenciaExterna.Trim(),
                ReferenciaConductor = input.ReferenciaConductor.Trim(),
                MontoEsperado = input.MontoEsperado,
                Moneda = input.Moneda.Trim().ToUpperInvariant(),
                MedioPago = input.MedioPago.Trim().ToUpperInvariant(),
                ReferenciaCuentaReceptora = input.ReferenciaCuentaReceptora.Trim(),
                // The existing field stores Yape's three-digit security code.
                OperacionDeclarada = input.CodigoSeguridad.Trim(),
                Estado = EstadoSolicitud.Pendiente,
                CodigoMotivo = "ESPERANDO_NOTIFICACION",
                FechaCreacion = now,
                FechaExpiracion = now.AddMinutes(configuration.GetValue("Yape:MinutosVigencia", 30))
            };
            db.SolicitudesVerificacion.Add(request);
            db.EventosAuditoria.Add(new EventoAuditoria
            {
                SolicitudVerificacion = request,
                TipoEvento = "SolicitudVerificacionCreada",
                Actor = "TonysGo",
                EstadoNuevo = EstadoSolicitud.Pendiente.ToString(),
                CodigoMotivo = "ESPERANDO_NOTIFICACION",
                IdCorrelacion = request.ReferenciaExterna
            });
            await db.SaveChangesAsync(ct);
            await TryResolveAsync(request, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Map(request);
        });
    }

    public async Task<VerificationResult?> FindAsync(string reference, CancellationToken ct)
    {
        ValidateReference(reference, nameof(reference));
        var request = await db.SolicitudesVerificacion.Include(x => x.EvidenciaAsignada)
            .SingleOrDefaultAsync(x => !x.EsSimulacion && x.ReferenciaExterna == reference, ct);
        if (request is null) return null;
        if (request.Estado is EstadoSolicitud.Pendiente or EstadoSolicitud.RequiereRevision
            && request.FechaExpiracion <= DateTimeOffset.UtcNow)
        {
            SetState(request, EstadoSolicitud.Expirada, "SOLICITUD_EXPIRADA", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        return Map(request);
    }

    private async Task<bool> TryResolveAsync(SolicitudVerificacion request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (request.FechaExpiracion <= now)
        {
            SetState(request, EstadoSolicitud.Expirada, "SOLICITUD_EXPIRADA", now);
            return false;
        }
        if (string.IsNullOrWhiteSpace(request.OperacionDeclarada)) return false;

        var sameClaimCount = await db.SolicitudesVerificacion.CountAsync(x => !x.EsSimulacion
            && x.Id != request.Id && x.OperacionDeclarada == request.OperacionDeclarada
            && x.MontoEsperado == request.MontoEsperado
            && x.ReferenciaCuentaReceptora == request.ReferenciaCuentaReceptora
            && x.FechaCreacion <= request.FechaExpiracion && x.FechaExpiracion >= request.FechaCreacion
            && x.Estado != EstadoSolicitud.Rechazada && x.Estado != EstadoSolicitud.Expirada, ct);
        if (sameClaimCount > 0)
        {
            SetState(request, EstadoSolicitud.RequiereRevision, "OPERACION_RECLAMADA_MAS_DE_UNA_VEZ", now);
            return false;
        }

        var toleranceStart = request.FechaCreacion.AddMinutes(-configuration.GetValue("Yape:MinutosToleranciaAnterior", 10));
        var candidates = await db.EvidenciasPago.Include(x => x.CorreoProcesado)
            .Where(x => !x.EsSimulacion && x.ReferenciaSolicitudExtraida == request.OperacionDeclarada
                && x.ProveedorPago == "YAPE" && x.CorreoProcesado.VersionExtractor == Extractor
                && x.FechaOperacion >= toleranceStart && x.FechaOperacion <= request.FechaExpiracion)
            .ToListAsync(ct);
        if (candidates.Count == 0)
        {
            SetState(request, EstadoSolicitud.Pendiente, "ESPERANDO_NOTIFICACION", now);
            return false;
        }

        var matches = candidates.Where(x => x.SolicitudVerificacionId is null
            && x.EstadoValidacion is EstadoEvidencia.Pendiente or EstadoEvidencia.RequiereRevision
            && x.Monto == request.MontoEsperado
            && string.Equals(x.Moneda, request.Moneda, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ReferenciaCuentaReceptora, request.ReferenciaCuentaReceptora, StringComparison.Ordinal)
            && x.DireccionMovimiento == DireccionMovimiento.Ingreso
            && x.EstadoPagoInformado == EstadoPagoInformado.Completado).ToArray();
        if (matches.Length != 1)
        {
            SetState(request, EstadoSolicitud.RequiereRevision,
                matches.Length > 1 ? "MULTIPLES_NOTIFICACIONES_COINCIDENTES" : "DATOS_NO_COINCIDEN", now);
            return false;
        }

        var evidence = matches[0];
        evidence.EstadoValidacion = EstadoEvidencia.Validada;
        evidence.CodigoMotivo = "CODIGO_MONTO_CUENTA_COINCIDEN";
        evidence.SolicitudVerificacion = request;
        SetState(request, EstadoSolicitud.Verificada, "CODIGO_MONTO_CUENTA_COINCIDEN", now);
        db.EventosAuditoria.Add(new EventoAuditoria
        {
            SolicitudVerificacion = request,
            CorreoProcesado = evidence.CorreoProcesado,
            TipoEvento = "PagoYapeVerificado",
            Actor = "VerificadorAutomatico",
            EstadoAnterior = EstadoSolicitud.Pendiente.ToString(),
            EstadoNuevo = EstadoSolicitud.Verificada.ToString(),
            CodigoMotivo = "CODIGO_MONTO_CUENTA_COINCIDEN",
            IdCorrelacion = request.ReferenciaExterna
        });
        return true;
    }

    private static void SetState(SolicitudVerificacion request, EstadoSolicitud state, string reason, DateTimeOffset now)
    {
        request.Estado = state;
        request.CodigoMotivo = reason;
        request.FechaResolucion = state is EstadoSolicitud.Verificada or EstadoSolicitud.Rechazada or EstadoSolicitud.Expirada ? now : null;
    }

    private void Validate(VerificationCreateRequest input)
    {
        ValidateReference(input.ReferenciaExterna, nameof(input.ReferenciaExterna));
        ValidateReference(input.ReferenciaConductor, nameof(input.ReferenciaConductor));
        ValidateReference(input.ReferenciaCuentaReceptora, nameof(input.ReferenciaCuentaReceptora));
        if (input.MontoEsperado <= 0 || input.MontoEsperado > 10_000m || decimal.Round(input.MontoEsperado, 2) != input.MontoEsperado)
            throw new YapeInputException("MONTO_INVALIDO", "El monto debe ser positivo y tener como maximo dos decimales.");
        if (!string.Equals(input.Moneda, "PEN", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(input.MedioPago, "YAPE", StringComparison.OrdinalIgnoreCase))
            throw new YapeInputException("MEDIO_PAGO_INVALIDO", "Solo se admite YAPE en moneda PEN.");
        var configuredAccount = configuration["Yape:CuentaReceptora"];
        if (string.IsNullOrWhiteSpace(configuredAccount))
            throw new YapeInputException("CUENTA_RECEPTORA_NO_CONFIGURADA", "Configura la cuenta receptora del Listener.");
        if (!string.Equals(configuredAccount, input.ReferenciaCuentaReceptora, StringComparison.Ordinal))
            throw new YapeInputException("CUENTA_RECEPTORA_INVALIDA", "La cuenta receptora no corresponde al Listener.");
        if (string.IsNullOrWhiteSpace(input.CodigoSeguridad)
            || !Regex.IsMatch(input.CodigoSeguridad, @"^[0-9]{3}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new YapeInputException("CODIGO_SEGURIDAD_INVALIDO", "El codigo de seguridad de Yape debe tener tres digitos.");
    }

    private static void ValidateReference(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100
            || !Regex.IsMatch(value, @"^[A-Za-z0-9._:@-]{1,100}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new YapeInputException("REFERENCIA_INVALIDA", $"{name} no tiene un formato valido.");
    }

    private static bool Same(SolicitudVerificacion entity, VerificationCreateRequest input) =>
        entity.ReferenciaConductor == input.ReferenciaConductor.Trim()
        && entity.MontoEsperado == input.MontoEsperado
        && string.Equals(entity.Moneda, input.Moneda.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(entity.MedioPago, input.MedioPago.Trim(), StringComparison.OrdinalIgnoreCase)
        && entity.ReferenciaCuentaReceptora == input.ReferenciaCuentaReceptora.Trim()
        && string.Equals(entity.OperacionDeclarada, input.CodigoSeguridad.Trim(), StringComparison.Ordinal);

    private static VerificationResult Map(SolicitudVerificacion request) => new(
        request.Id, request.ReferenciaExterna, request.ReferenciaConductor, request.MontoEsperado,
        request.Moneda, request.MedioPago, request.OperacionDeclarada!, request.Estado.ToString(),
        request.CodigoMotivo ?? "SIN_MOTIVO", request.FechaCreacion, request.FechaExpiracion,
        request.FechaResolucion, request.EvidenciaAsignada?.Id);

    private static string? Clean(string? value, int max) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim().Length > max ? value.Trim()[..max] : value.Trim();
}
