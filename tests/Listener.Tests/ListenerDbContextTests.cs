using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Listener.Tests;

public class ListenerDbContextTests
{
    private static ListenerDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ListenerDbContext>()
            .UseSqlServer("Server=.\\SQLEXPRESS;Database=ListenerDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void ModelIncludesListenerHistoryAndBbvaAccumulationTables()
    {
        using var db = CreateContext();
        var tables = db.Model.GetEntityTypes().Select(x => x.GetTableName()).Order().ToArray();
        Assert.Equal(new[] { "BbvaPaymentBalances", "BbvaPaymentValidations", "CorreosProcesados", "EstadoSincronizacionCorreo", "EventosAuditoria", "EvidenciasPago", "SolicitudesVerificacion" }, tables);
    }

    [Fact]
    public void MoneyUsesDecimalAndMutableEntitiesHaveConcurrencyTokens()
    {
        using var db = CreateContext();
        var request = db.Model.FindEntityType(typeof(SolicitudVerificacion))!;
        var amount = request.FindProperty(nameof(SolicitudVerificacion.MontoEsperado))!;
        Assert.Equal(18, amount.GetPrecision());
        Assert.Equal(2, amount.GetScale());
        foreach (var type in new[] { typeof(SolicitudVerificacion), typeof(CorreoProcesado), typeof(EvidenciaPago), typeof(EstadoSincronizacionCorreo), typeof(BbvaPaymentBalance) })
            Assert.True(db.Model.FindEntityType(type)!.FindProperty("VersionFila")!.IsConcurrencyToken);
    }

    [Fact]
    public void UntrustedEvidenceDoesNotReservePaymentIdentity()
    {
        using var db = CreateContext();
        var evidence = db.Model.FindEntityType(typeof(EvidenciaPago))!;
        var operationIndex = Assert.Single(evidence.GetIndexes(), x =>
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "ProveedorPago", "ReferenciaCuentaReceptora", "NumeroOperacion" }));
        Assert.True(operationIndex.IsUnique);
        Assert.Equal("[EstadoValidacion] = 'Validada'", operationIndex.GetFilter());
    }

    [Fact]
    public void ForeignKeysNeverCascadeDeleteEvidenceOrAudit()
    {
        using var db = CreateContext();
        Assert.All(db.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()),
            fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }
}
