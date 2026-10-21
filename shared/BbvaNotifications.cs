using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TonysGo.Listener.Bbva;

public sealed record BbvaNotificationRequest(
    string NotificationKey, string DeviceId, string PackageName,
    DateTimeOffset PostedAt, string? Title, string? Text, string? BigText);

public sealed record BbvaPaymentData(bool EsAbono, string? NombreCompleto, decimal? Monto, string CodigoMotivo);

public sealed record BbvaIngestResult(Guid Id, bool Duplicada, bool EsAbono,
    string? NombreCompleto, decimal? Monto, string CodigoMotivo, string EstadoEnvio);

public static partial class BbvaNotificationParser
{
    // Official listing: https://play.google.com/store/apps/details?id=com.bbva.GEMA.global
    public const string OfficialPackage = "com.bbva.GEMA.global";

    public static BbvaPaymentData Parse(BbvaNotificationRequest request)
    {
        if (!string.Equals(request.PackageName, OfficialPackage, StringComparison.Ordinal))
            return new(false, null, null, "PAQUETE_NO_PERMITIDO");
        if (!string.Equals(Normalize(request.Title ?? ""), "Cobro con QR", StringComparison.OrdinalIgnoreCase))
            return new(false, null, null, "NO_ES_COBRO_QR");
        var payments = new List<(string Name, decimal Amount)>();
        foreach (var body in new[] { request.Text, request.BigText }.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            // Text may be shortened; bigText contains the expanded body.
            var match = PaymentRegex().Match(Normalize(body!));
            if (!match.Success || !decimal.TryParse(match.Groups["amount"].Value,
                    NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out var amount)
                || amount <= 0 || amount > 9_999_999_999_999_999.99m) continue;
            var name = match.Groups["name"].Value.Trim();
            if (!payments.Any(x => x.Amount == amount && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                payments.Add((name, amount));
        }
        return payments.Count switch
        {
            1 => new(true, payments[0].Name, payments[0].Amount, "ABONO_BBVA_EXTRAIDO"),
            > 1 => new(false, null, null, "DATOS_BBVA_AMBIGUOS"),
            _ => new(false, null, null, "NOMBRE_O_MONTO_NO_EXTRAIDO")
        };
    }

    // Expanding one notification is not a second payment. Do not hash its changing text.
    public static string EventId(string notificationKey, long postedAt) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{OfficialPackage}|{notificationKey}|{postedAt}")))
            .ToLowerInvariant();
    private static string Normalize(string text) => WhitespaceRegex().Replace(text, " ").Trim();

    [GeneratedRegex(@"^(?<name>[\p{L}][\p{L}\p{M} .'’\-]{1,199}?)\s+te\s+hizo\s+un\s+pago\s+de\s+S/\s*(?<amount>(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)(?:\.[0-9]{1,2})?)\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PaymentRegex();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex WhitespaceRegex();
}
