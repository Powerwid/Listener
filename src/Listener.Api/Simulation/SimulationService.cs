using System.Data;
using System.Security.Cryptography;
using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace Listener.Api.Simulation;

public sealed class SimulationService(DbContextOptions<ListenerDbContext> options)
{
    public async Task<SimulationResult> ImportAsync(SimulationRequest request, byte[] eml, CancellationToken ct = default)
    {
        request.Validate();
        if (eml.Length is 0 or > 131072)
            throw new SimulationInputException("ARCHIVO_INVALIDO", "El correo demo debe tener entre 1 byte y 128 KiB.");
        using var stream = new MemoryStream(eml, writable: false);
        using var message = await MimeMessage.LoadAsync(stream, ct);
        var extracted = DemoEmailParser.Parse(message);
        var hash = Convert.ToHexString(SHA256.HashData(eml));
        var differences = DemoEmailParser.Compare(request, extracted);

        await using var strategyContext = new ListenerDbContext(options);
        return await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // Rebuild context on every retry, including an uncertain prior commit.
            await using var db = new ListenerDbContext(options);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var existing = await db.SolicitudesVerificacion.SingleOrDefaultAsync(x => x.ReferenciaExterna == request.ReferenciaExterna, ct);
            if (existing is not null)
            {
                if (!existing.EsSimulacion || ToInput(existing) != request)
                    throw new SimulationInputException("REFERENCIA_EN_CONFLICTO", "Esa referencia ya existe con otros datos o no es una simulacion. Usa una referencia DEMO- nueva.");
                var stored = await LoadAsync(db, existing, ct);
                if (stored.Mail.HashContenido != hash)
                    throw new SimulationInputException("CORREO_EN_CONFLICTO", "La solicitud ya tiene otro correo de prueba. Para cambiar el caso, usa otra referencia DEMO-.");
                return ToResult(existing, stored.Mail, stored.Evidence);
            }

            var cursor = await db.EstadoSincronizacionCorreo.SingleOrDefaultAsync(
                x => x.Buzon == "simulacion@listener.invalid" && x.Carpeta == "SIMULACION", ct);
            if (cursor is null)
            {
                cursor = new() { Buzon = "simulacion@listener.invalid", Carpeta = "SIMULACION", UidValidity = 1 };
                db.EstadoSincronizacionCorreo.Add(cursor);
            }
            if (cursor.UltimoUidProcesado >= uint.MaxValue)
                throw new SimulationInputException("SIMULADOR_AGOTADO", "Se agoto el espacio de identificadores del simulador.");
            cursor.UltimoUidProcesado++;
            var now = DateTimeOffset.UtcNow;
            var code = differences.Length == 0 ? "SIMULACION_COINCIDE" : "SIMULACION_NO_COINCIDE";
            var entity = new SolicitudVerificacion
            {
                EsSimulacion = true, ReferenciaExterna = request.ReferenciaExterna,
                ReferenciaConductor = request.ReferenciaConductor, MontoEsperado = request.MontoEsperado,
                Moneda = request.Moneda, MedioPago = request.MedioPago,
                ReferenciaCuentaReceptora = request.ReferenciaCuentaReceptora, OperacionDeclarada = request.OperacionDeclarada,
                Estado = EstadoSolicitud.RequiereRevision, CodigoMotivo = code,
                FechaCreacion = now, FechaExpiracion = now.AddHours(1)
            };
            var mail = new CorreoProcesado
            {
                EsSimulacion = true, EstadoSincronizacionCorreo = cursor, UidValidity = 1, Uid = cursor.UltimoUidProcesado,
                // These are asserted headers, not authenticated sender information.
                MessageId = Cut(message.MessageId, 998), Remitente = Cut(message.From.ToString(), 320),
                Asunto = Cut(message.Subject, 1000), FechaRecepcion = now, HashContenido = hash,
                ResultadoAutenticidad = ResultadoAutenticidad.NoVerificada,
                EstadoProcesamiento = EstadoProcesamientoCorreo.Procesado, CodigoMotivo = "SOLO_SIMULACION",
                VersionExtractor = "listener-demo-v1"
            };
            var evidence = new EvidenciaPago
            {
                EsSimulacion = true, CorreoProcesado = mail,
                ReferenciaSolicitudExtraida = extracted.ReferenciaSolicitud,
                ReferenciaPagador = extracted.ReferenciaConductor, ProveedorPago = extracted.Proveedor,
                NumeroOperacion = extracted.NumeroOperacion, ReferenciaCuentaReceptora = extracted.CuentaReceptora,
                Monto = extracted.Monto, Moneda = extracted.Moneda, FechaOperacion = extracted.FechaOperacion,
                DireccionMovimiento = extracted.Direccion == "INGRESO" ? DireccionMovimiento.Ingreso : DireccionMovimiento.Salida,
                EstadoPagoInformado = Enum.Parse<EstadoPagoInformado>(extracted.EstadoPago, ignoreCase: true),
                EstadoValidacion = EstadoEvidencia.RequiereRevision, CodigoMotivo = code
                // No assignment to a real payment claim, even when fixture fields match.
            };
            db.AddRange(entity, evidence, new EventoAuditoria
            {
                SolicitudVerificacion = entity, CorreoProcesado = mail, Actor = "simulador-local",
                TipoEvento = "SimulacionComparada", EstadoNuevo = "RequiereRevision", CodigoMotivo = code,
                IdCorrelacion = entity.Id.ToString()
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ToResult(entity, mail, evidence);
        });
    }

    public async Task<SimulationResult> GetAsync(string reference, CancellationToken ct = default)
    {
        DemoValidation.Reference(reference, "referenciaExterna");
        await using var db = new ListenerDbContext(options);
        var request = await db.SolicitudesVerificacion.AsNoTracking()
            .SingleOrDefaultAsync(x => x.EsSimulacion && x.ReferenciaExterna == reference, ct)
            ?? throw new SimulationInputException("NO_ENCONTRADA", "No existe esa solicitud de simulacion.");
        var stored = await LoadAsync(db, request, ct);
        return ToResult(request, stored.Mail, stored.Evidence);
    }

    private static async Task<(CorreoProcesado Mail, EvidenciaPago Evidence)> LoadAsync(
        ListenerDbContext db, SolicitudVerificacion request, CancellationToken ct)
    {
        var audit = await db.EventosAuditoria.AsNoTracking().SingleOrDefaultAsync(
            x => x.SolicitudVerificacionId == request.Id && x.TipoEvento == "SimulacionComparada", ct)
            ?? throw new SimulationInputException("SIMULACION_INCOMPLETA", "No se encontro la evidencia de esa simulacion.");
        var mail = await db.CorreosProcesados.AsNoTracking().SingleAsync(x => x.Id == audit.CorreoProcesadoId && x.EsSimulacion, ct);
        var evidence = await db.EvidenciasPago.AsNoTracking().SingleAsync(x => x.CorreoProcesadoId == mail.Id && x.EsSimulacion, ct);
        return (mail, evidence);
    }

    private static SimulationRequest ToInput(SolicitudVerificacion r) => new(
        r.ReferenciaExterna, r.ReferenciaConductor, r.MontoEsperado, r.Moneda,
        r.MedioPago, r.ReferenciaCuentaReceptora, r.OperacionDeclarada!);

    private static SimulationResult ToResult(SolicitudVerificacion r, CorreoProcesado mail, EvidenciaPago e)
    {
        var extracted = new DemoMailData(e.ReferenciaSolicitudExtraida!, e.ReferenciaPagador!, e.Monto!.Value,
            e.Moneda!, e.ProveedorPago!, e.ReferenciaCuentaReceptora!, e.NumeroOperacion!, e.FechaOperacion!.Value,
            e.DireccionMovimiento.ToString().ToUpperInvariant(), e.EstadoPagoInformado.ToString().ToUpperInvariant());
        var differences = DemoEmailParser.Compare(ToInput(r), extracted);
        return new(r.Id, r.ReferenciaExterna, differences.Length == 0, differences, mail.Id, e.Id, extracted);
    }

    private static string? Cut(string? value, int max) => value?.Length > max ? value[..max] : value;
}
