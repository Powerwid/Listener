using Listener.Api.Bbva;
using TonysGo.Listener.Bbva;

namespace Listener.Tests;

public class BbvaNotificationTests
{
    internal static BbvaNotificationRequest Notice(string? text = null) => new(
        "bbva-event-01", "phone-01", BbvaNotificationParser.OfficialPackage,
        DateTimeOffset.UtcNow, "Cobro con QR",
        text ?? "VICTOR RAUL MAMANI QUEA te hizo un pago de S/ 20.0", null);

    [Fact]
    public void ExtractsTheAttachedBbvaFormatWithoutASecurityCode()
    {
        var payment = BbvaNotificationParser.Parse(Notice());
        Assert.True(payment.EsAbono);
        Assert.Equal("VICTOR RAUL MAMANI QUEA", payment.NombreCompleto);
        Assert.Equal(20.00m, payment.Monto);
    }

    [Theory]
    [InlineData("S/ 20", "20")]
    [InlineData("S/ 20.0", "20")]
    [InlineData("S/ 20.50", "20.50")]
    [InlineData("S/ 1,250.50", "1250.50")]
    public void ReadsExactSolesAmountsAndPreservesTheFullName(string input, string expected)
    {
        var payment = BbvaNotificationParser.Parse(Notice($"  JOSÉ  LUIS\nMUÑOZ PÉREZ te hizo un pago de\n{input}  "));
        Assert.True(payment.EsAbono);
        Assert.Equal("JOSÉ LUIS MUÑOZ PÉREZ", payment.NombreCompleto);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), payment.Monto);
    }

    [Theory]
    [InlineData("S/ 0")]
    [InlineData("S/ -20")]
    [InlineData("S/ 20.123")]
    [InlineData("S/ 20,50")]
    [InlineData("USD 20.00")]
    [InlineData("S/ 20.00 y S/ 30.00")]
    [InlineData("S/ 10000000000000000")]
    public void DoesNotForwardMalformedOrAmbiguousAmounts(string amount)
    {
        Assert.False(BbvaNotificationParser.Parse(Notice($"VICTOR RAUL MAMANI QUEA te hizo un pago de {amount}")).EsAbono);
    }

    [Fact]
    public void ExpandedNotificationCanCompleteTheShortenedBody()
    {
        var input = Notice() with { Text = "VICTOR RAUL MAMANI…", BigText = Notice().Text };
        Assert.True(BbvaNotificationParser.Parse(input).EsAbono);
        Assert.True(BbvaNotificationParser.Parse(Notice() with { BigText = Notice().Text }).EsAbono);
    }

    [Fact]
    public void ConflictingCompleteBodiesAreNotForwarded()
    {
        var input = Notice() with { BigText = "OTRA PERSONA te hizo un pago de S/ 30.00" };
        Assert.Equal("DATOS_BBVA_AMBIGUOS", BbvaNotificationParser.Parse(input).CodigoMotivo);
    }

    [Theory]
    [InlineData("com.bcp.innovacxion.yapeapp", "Cobro con QR")]
    [InlineData("com.example.fake", "Cobro con QR")]
    [InlineData(BbvaNotificationParser.OfficialPackage, "Transferencia enviada")]
    [InlineData(BbvaNotificationParser.OfficialPackage, "Código de verificación")]
    public void RequiresTheBbvaPackageAndIncomingQrTitle(string package, string title)
    {
        Assert.False(BbvaNotificationParser.Parse(Notice() with { PackageName = package, Title = title }).EsAbono);
    }

    [Fact]
    public void DistinctNotificationsFromTheSamePayerAndAmountKeepSeparateIdentities()
    {
        Assert.Equal(BbvaNotificationParser.EventId("android-key", 100), BbvaNotificationParser.EventId("android-key", 100));
        Assert.NotEqual(BbvaNotificationParser.EventId("android-key", 100), BbvaNotificationParser.EventId("android-key", 101));
    }

    [Fact]
    public void OfflineQueueCanDeliverOldNoticesForTonysGoToEvaluate()
    {
        BbvaNotificationService.Validate(Notice() with { PostedAt = DateTimeOffset.UtcNow.AddDays(-7) });
        Assert.Throws<BbvaInputException>(() => BbvaNotificationService.Validate(Notice() with { PostedAt = DateTimeOffset.UtcNow.AddHours(1) }));
        Assert.Throws<BbvaInputException>(() => BbvaNotificationService.Validate(Notice() with { PackageName = "com.example.fake" }));
    }
}
