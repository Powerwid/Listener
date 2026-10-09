using Listener.Api.Yape;

namespace Listener.Tests;

public class YapeNotificationParserTests
{
    [Fact]
    public void ExtractsIncomingAmountAndOperationFromOfficialPackage()
    {
        var result = YapeNotificationParser.Parse(new(
            "event-001", "phone-001", YapeNotificationParser.OfficialPackage,
            DateTimeOffset.UtcNow, "Yapeo recibido",
            "Recibiste S/ 25.50 de Maria Perez. Codigo de seguridad: 837", null));

        Assert.True(result.EsIngreso);
        Assert.Equal(25.50m, result.Monto);
        Assert.Equal("837", result.CodigoSeguridad);
        Assert.Equal("NOTIFICACION_LISTA_PARA_COMPARAR", result.CodigoMotivo);
    }

    [Fact]
    public void NeverAcceptsSameTextFromAnotherPackage()
    {
        var result = YapeNotificationParser.Parse(new(
            "event-002", "phone-001", "com.example.fake",
            DateTimeOffset.UtcNow, "Yapeo recibido",
            "Recibiste S/ 25.50. Codigo de seguridad: 837", null));

        Assert.False(result.EsIngreso);
        Assert.Equal("PAQUETE_NO_PERMITIDO", result.CodigoMotivo);
    }

    [Fact]
    public void DoesNotTreatOutgoingYapeAsIncome()
    {
        var result = YapeNotificationParser.Parse(new(
            "event-003", "phone-001", YapeNotificationParser.OfficialPackage,
            DateTimeOffset.UtcNow, "Pago realizado",
            "Yapeaste S/ 25.50. Codigo de seguridad: 837", null));

        Assert.False(result.EsIngreso);
        Assert.Equal("NO_ES_YAPEO_RECIBIDO", result.CodigoMotivo);
    }

    [Fact]
    public void KeepsNotificationPendingWhenOperationIsMissing()
    {
        var result = YapeNotificationParser.Parse(new(
            "event-004", "phone-001", YapeNotificationParser.OfficialPackage,
            DateTimeOffset.UtcNow, "Yapeo recibido", "Te yapearon S/ 10.00", null));

        Assert.True(result.EsIngreso);
        Assert.Equal(10m, result.Monto);
        Assert.Null(result.CodigoSeguridad);
        Assert.Equal("CODIGO_NO_PRESENTE_EN_NOTIFICACION", result.CodigoMotivo);
    }

    [Fact]
    public void RecognizesCurrentYapePaymentConfirmationFormat()
    {
        var result = YapeNotificationParser.Parse(new(
            "event-006", "phone-001", YapeNotificationParser.OfficialPackage,
            DateTimeOffset.UtcNow, "Confirmación de Pago",
            "Yape! MARIA PEREZ te envió un pago por S/ 1.50",
            "Yape! MARIA PEREZ te envió un pago por S/ 1.50"));

        Assert.True(result.EsIngreso);
        Assert.Equal(1.50m, result.Monto);
        Assert.Equal("MARIA PEREZ", result.Pagador);
        Assert.Null(result.CodigoSeguridad);
        Assert.Equal("CODIGO_NO_PRESENTE_EN_NOTIFICACION", result.CodigoMotivo);
    }

    [Fact]
    public void RecognizesAbbreviatedSecurityCodeFromYapeAppNotification()
    {
        const string message = "Ana Per* te envió un pago por S/ 10. El cód. de seguridad es: 123";
        var result = YapeNotificationParser.Parse(new(
            "event-app-001", "phone-001", YapeNotificationParser.OfficialPackage,
            DateTimeOffset.UtcNow, "Confirmación de Pago", message, message));

        Assert.True(result.EsIngreso);
        Assert.Equal(10m, result.Monto);
        Assert.Equal("123", result.CodigoSeguridad);
        Assert.Equal("Ana Per*", result.Pagador);
        Assert.Equal("NOTIFICACION_LISTA_PARA_COMPARAR", result.CodigoMotivo);
    }

    [Fact]
    public void RejectsStaleReplayEnvelope()
    {
        Assert.Throws<YapeInputException>(() => YapeNotificationParser.Parse(new(
            "event-005", "phone-001", YapeNotificationParser.OfficialPackage,
            DateTimeOffset.UtcNow.AddDays(-3), "Yapeo recibido", "Recibiste S/ 10.00", null)));
    }
}
