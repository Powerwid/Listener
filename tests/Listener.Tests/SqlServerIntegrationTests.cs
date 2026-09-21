using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Listener.Tests;

// Opt-in local integration tests. Each test rolls back every write and never
// creates/drops a database or applies migrations. Not connected to TonysGo.
public sealed class LocalSqlFactAttribute : FactAttribute
{
    public LocalSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LISTENER_RUN_SQL_TESTS") != "1")
            Skip = "Set LISTENER_RUN_SQL_TESTS=1 to test the migrated local ListenerDb with rollback.";
    }
}

public class SqlServerIntegrationTests
{
    private static ListenerDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ListenerDbContext>()
            .UseSqlServer("Server=.\\SQLEXPRESS;Database=ListenerDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5")
            .Options);

    private static SolicitudVerificacion Request(string? reference = null) => new()
    {
        ReferenciaExterna = reference ?? $"test-{Guid.NewGuid():N}",
        ReferenciaConductor = "test-conductor",
        MontoEsperado = 25.50m,
        MedioPago = "YAPE",
        ReferenciaCuentaReceptora = "test-receptor",
        FechaExpiracion = DateTimeOffset.UtcNow.AddMinutes(30)
    };

    private static CorreoProcesado Mail() => new()
    {
        EstadoSincronizacionCorreo = new() { Buzon = $"test-{Guid.NewGuid():N}@example.invalid" },
        Uid = uint.MaxValue,
        UidValidity = uint.MaxValue,
        FechaRecepcion = DateTimeOffset.UtcNow,
        HashContenido = new string('a', 64)
    };

    private static EvidenciaPago Evidence(CorreoProcesado mail, string operation, EstadoEvidencia state) => new()
    {
        CorreoProcesado = mail,
        ProveedorPago = "TEST",
        NumeroOperacion = operation,
        ReferenciaCuentaReceptora = "test-receptor",
        Monto = 25.50m,
        Moneda = "PEN",
        FechaOperacion = DateTimeOffset.UtcNow,
        DireccionMovimiento = DireccionMovimiento.Ingreso,
        EstadoPagoInformado = EstadoPagoInformado.Completado,
        EstadoValidacion = state
    };

    private static async Task AssertSqlFailure(ListenerDbContext db, params int[] numbers)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number, numbers);
    }

    [LocalSqlFact]
    public async Task SavesAndReadsAllFiveEntitiesWithoutLeavingData()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var request = Request();
        var mail = Mail();
        var evidence = Evidence(mail, Guid.NewGuid().ToString("N"), EstadoEvidencia.Pendiente);
        db.AddRange(request, evidence, new EventoAuditoria
        {
            SolicitudVerificacion = request, CorreoProcesado = mail,
            Actor = "integration-test", TipoEvento = "Prueba", CodigoMotivo = "TEST",
            IdCorrelacion = Guid.NewGuid().ToString("N")
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var saved = await db.SolicitudesVerificacion.SingleAsync(x => x.Id == request.Id);
        Assert.Equal(25.50m, saved.MontoEsperado);
        Assert.Equal(EstadoSolicitud.Pendiente, saved.Estado);
        Assert.Equal(8, saved.VersionFila.Length);
        Assert.Equal((long)uint.MaxValue, (await db.CorreosProcesados.SingleAsync(x => x.Id == mail.Id)).Uid);
        Assert.True(await db.EstadoSincronizacionCorreo.AnyAsync(x => x.Id == mail.EstadoSincronizacionCorreoId));
        Assert.True(await db.EvidenciasPago.AnyAsync(x => x.Id == evidence.Id));
        Assert.True(await db.EventosAuditoria.AnyAsync(x => x.SolicitudVerificacionId == request.Id));
    }

    [LocalSqlFact]
    public async Task RejectsRepeatedExternalRequestReference()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var reference = Guid.NewGuid().ToString("N");
        db.Add(Request(reference));
        await db.SaveChangesAsync();
        db.Add(Request(reference));
        await AssertSqlFailure(db, 2601, 2627);
    }

    [LocalSqlFact]
    public async Task RejectsRepeatedImapMessageButAllowsNewUidValidity()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var mail = Mail();
        db.Add(mail);
        await db.SaveChangesAsync();
        var second = Mail();
        second.EstadoSincronizacionCorreo = mail.EstadoSincronizacionCorreo;
        second.UidValidity = 1;
        db.Add(second);
        await db.SaveChangesAsync();
        var duplicate = Mail();
        duplicate.EstadoSincronizacionCorreo = mail.EstadoSincronizacionCorreo;
        db.Add(duplicate);
        await AssertSqlFailure(db, 2601, 2627);
    }

    [LocalSqlFact]
    public async Task UntrustedEvidenceCannotBlockRealPaymentButValidatedDuplicatesAreRejected()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var mail = Mail();
        var operation = Guid.NewGuid().ToString("N");
        db.Add(Evidence(mail, operation, EstadoEvidencia.Invalida));
        await db.SaveChangesAsync();
        db.Add(Evidence(mail, operation, EstadoEvidencia.Validada));
        await db.SaveChangesAsync();
        db.Add(Evidence(mail, operation, EstadoEvidencia.Validada));
        await AssertSqlFailure(db, 2601, 2627);
    }

    [LocalSqlFact]
    public async Task RejectsTwoAssignedPaymentsForTheSameRequest()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var request = Request();
        var first = Evidence(Mail(), Guid.NewGuid().ToString("N"), EstadoEvidencia.Validada);
        first.SolicitudVerificacion = request;
        db.Add(first);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var second = Evidence(Mail(), Guid.NewGuid().ToString("N"), EstadoEvidencia.Validada);
        second.SolicitudVerificacionId = request.Id;
        db.Add(second);
        await AssertSqlFailure(db, 2601, 2627);
    }

    [LocalSqlFact]
    public async Task RejectsAssigningUnvalidatedEvidence()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var evidence = Evidence(Mail(), Guid.NewGuid().ToString("N"), EstadoEvidencia.Pendiente);
        evidence.SolicitudVerificacion = Request();
        db.Add(evidence);
        await AssertSqlFailure(db, 547);
    }

    [LocalSqlFact]
    public async Task RejectsZeroAmount()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var request = Request();
        request.MontoEsperado = 0;
        db.Add(request);
        await AssertSqlFailure(db, 547);
    }

    [LocalSqlFact]
    public async Task RejectsIncompleteValidatedPayment()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var evidence = Evidence(Mail(), Guid.NewGuid().ToString("N"), EstadoEvidencia.Validada);
        evidence.NumeroOperacion = null;
        db.Add(evidence);
        await AssertSqlFailure(db, 547);
    }

    [LocalSqlFact]
    public async Task RejectsStaleRowVersion()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var request = Request();
        db.Add(request);
        await db.SaveChangesAsync();
        var staleCopy = await db.SolicitudesVerificacion.AsNoTracking().SingleAsync(x => x.Id == request.Id);
        request.CodigoMotivo = "FIRST_UPDATE";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        staleCopy.CodigoMotivo = "STALE_UPDATE";
        db.Update(staleCopy);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }

    [LocalSqlFact]
    public async Task SimulationRequestCannotBeVerifiedEvenByDirectDatabaseWrite()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var request = Request();
        request.EsSimulacion = true;
        request.Estado = EstadoSolicitud.Verificada;
        request.FechaResolucion = DateTimeOffset.UtcNow;
        db.Add(request);
        await AssertSqlFailure(db, 547);
    }

    [LocalSqlFact]
    public async Task SimulationEmailCannotClaimAuthenticity()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var mail = Mail();
        mail.EsSimulacion = true;
        mail.ResultadoAutenticidad = ResultadoAutenticidad.Valida;
        db.Add(mail);
        await AssertSqlFailure(db, 547);
    }

    [LocalSqlFact]
    public async Task SimulationEvidenceCannotReserveRealOperation()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var evidence = Evidence(Mail(), Guid.NewGuid().ToString("N"), EstadoEvidencia.Validada);
        evidence.EsSimulacion = true;
        db.Add(evidence);
        await AssertSqlFailure(db, 547);
    }
}
