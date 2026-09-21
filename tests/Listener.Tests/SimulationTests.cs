using System.Text.Json;
using Listener.Api.Simulation;
using MimeKit;

namespace Listener.Tests;

public class SimulationTests
{
    private const string RequestJson = """
        {
          "referenciaExterna": "DEMO-UNIT-001",
          "referenciaConductor": "CONDUCTOR-001",
          "montoEsperado": 25.50,
          "moneda": "PEN",
          "medioPago": "YAPE",
          "referenciaCuentaReceptora": "TONYSGO-PRUEBA",
          "operacionDeclarada": "OP-001"
        }
        """;

    private static MimeMessage Fixture() => new()
    {
        Headers = { { "X-Listener-Demo", "v1" } },
        Body = new TextPart("plain")
        {
            Text = """
                REFERENCIA_SOLICITUD: DEMO-UNIT-001
                REFERENCIA_CONDUCTOR: CONDUCTOR-001
                PROVEEDOR: YAPE
                NUMERO_OPERACION: OP-001
                CUENTA_RECEPTORA: TONYSGO-PRUEBA
                MONTO: 25.50
                MONEDA: PEN
                FECHA_OPERACION: 2026-09-04T12:00:00Z
                DIRECCION: INGRESO
                ESTADO: COMPLETADO
                """
        }
    };

    [Fact]
    public void ParsesPlainMimeAndComparesAllFixtureFields()
    {
        using var message = Fixture();
        var data = DemoEmailParser.Parse(message);
        Assert.Equal(25.50m, data.Monto);
        Assert.Equal(TimeSpan.Zero, data.FechaOperacion.Offset);
        Assert.Empty(DemoEmailParser.Compare(SimulationRequest.Parse(RequestJson), data));
    }

    [Fact]
    public void MatchingFixtureNeverBecomesVerifiedPayment()
    {
        using var message = Fixture();
        var result = new SimulationResult(Guid.NewGuid(), "DEMO-UNIT-001", true, [], Guid.NewGuid(), Guid.NewGuid(), DemoEmailParser.Parse(message));
        Assert.True(result.EsSimulacion);
        Assert.False(result.PagoVerificado);
        Assert.Equal("RequiereRevision", result.Estado);
        Assert.Equal("NoVerificada", result.AutenticidadCorreo);
    }

    [Theory]
    [InlineData("MONTO: 25.50", "MONTO: 30.00", "MONTO")]
    [InlineData("MONEDA: PEN", "MONEDA: USD", "MONEDA")]
    [InlineData("PROVEEDOR: YAPE", "PROVEEDOR: BCP", "PROVEEDOR")]
    [InlineData("NUMERO_OPERACION: OP-001", "NUMERO_OPERACION: OP-002", "NUMERO_OPERACION")]
    [InlineData("CUENTA_RECEPTORA: TONYSGO-PRUEBA", "CUENTA_RECEPTORA: OTRA", "CUENTA_RECEPTORA")]
    [InlineData("REFERENCIA_CONDUCTOR: CONDUCTOR-001", "REFERENCIA_CONDUCTOR: CONDUCTOR-002", "REFERENCIA_CONDUCTOR")]
    [InlineData("REFERENCIA_SOLICITUD: DEMO-UNIT-001", "REFERENCIA_SOLICITUD: DEMO-UNIT-002", "REFERENCIA_SOLICITUD")]
    [InlineData("DIRECCION: INGRESO", "DIRECCION: SALIDA", "NO_ES_INGRESO")]
    [InlineData("ESTADO: COMPLETADO", "ESTADO: PENDIENTE", "PAGO_NO_COMPLETADO")]
    [InlineData("ESTADO: COMPLETADO", "ESTADO: REVERSADO", "PAGO_NO_COMPLETADO")]
    public void ReportsMismatches(string before, string after, string expected)
    {
        using var message = Fixture();
        var body = Assert.IsType<TextPart>(message.Body);
        body.Text = body.Text.Replace(before, after);
        Assert.Equal(new[] { expected }, DemoEmailParser.Compare(SimulationRequest.Parse(RequestJson), DemoEmailParser.Parse(message)));
    }

    [Theory]
    [InlineData("MONTO: 25.50", "MONTO: 25,50")]
    [InlineData("MONTO: 25.50", "MONTO: -25.50")]
    [InlineData("MONTO: 25.50", "MONTO: 25.501")]
    [InlineData("MONTO: 25.50", "MONTO: 0")]
    [InlineData("MONTO: 25.50", "MONTO: 25.50\nMONTO: 30.00")]
    [InlineData("MONTO: 25.50", "")]
    [InlineData("MONEDA: PEN", "MONEDA: EUR")]
    [InlineData("ESTADO: COMPLETADO", "ESTADO: DESCONOCIDO")]
    [InlineData("FECHA_OPERACION: 2026-09-04T12:00:00Z", "FECHA_OPERACION: 04/09/2026")]
    public void RejectsAmbiguousOrMalformedFixtures(string before, string after)
    {
        using var message = Fixture();
        var body = Assert.IsType<TextPart>(message.Body);
        body.Text = body.Text.Replace(before, after);
        Assert.Throws<SimulationInputException>(() => DemoEmailParser.Parse(message));
    }

    [Fact]
    public void RejectsRealMailWithoutFixtureMarker()
    {
        using var message = Fixture();
        message.Headers.RemoveAll("X-Listener-Demo");
        Assert.Throws<SimulationInputException>(() => DemoEmailParser.Parse(message));
    }

    [Fact]
    public void RejectsHtmlAndDoesNotFetchRemoteContent()
    {
        using var message = Fixture();
        message.Body = new TextPart("html") { Text = "<img src='https://example.invalid/tracker'>" };
        Assert.Throws<SimulationInputException>(() => DemoEmailParser.Parse(message));
    }

    [Fact]
    public void RejectsAttemptsToInjectApprovalFields()
    {
        Assert.Throws<JsonException>(() => SimulationRequest.Parse(RequestJson.Replace("\"montoEsperado\": 25.50,", "\"montoEsperado\": 25.50, \"estado\": \"Verificada\",")));
    }

    [Fact]
    public void RejectsDuplicateJsonFields()
    {
        Assert.Throws<SimulationInputException>(() => SimulationRequest.Parse(RequestJson.Replace("\"montoEsperado\": 25.50,", "\"montoEsperado\": 25.50, \"MontoEsperado\": 50,")));
    }

    [Theory]
    [InlineData("DEMO-UNIT-001", "REAL-001")]
    [InlineData("CONDUCTOR-001", "")]
    [InlineData("25.50", "0")]
    [InlineData("25.50", "25.501")]
    public void RejectsInvalidRequests(string before, string after)
    {
        Assert.Throws<SimulationInputException>(() => SimulationRequest.Parse(RequestJson.Replace(before, after)));
    }

    [Theory]
    [InlineData("Server=remote.example;Database=ListenerDb;Integrated Security=True")]
    [InlineData("Server=.\\SQLEXPRESS;Database=AppTaxi;Integrated Security=True")]
    [InlineData("Server=.\\SQLEXPRESS;Database=ListenerDb;Integrated Security=False")]
    public void RefusesRemoteOrUnrelatedDatabase(string connection)
    {
        Assert.Throws<SimulationInputException>(() => SimulationCommand.EnsureLocalDatabase(connection));
    }

    [Fact]
    public void AllowsLocalListenerDatabase()
    {
        SimulationCommand.EnsureLocalDatabase("Server=.\\SQLEXPRESS;Database=ListenerDb;Integrated Security=True");
    }
}
