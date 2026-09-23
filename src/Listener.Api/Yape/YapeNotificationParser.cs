using System.Globalization;
using System.Text.RegularExpressions;

namespace Listener.Api.Yape;

public static partial class YapeNotificationParser
{
    public const string OfficialPackage = "com.bcp.innovacxion.yapeapp";

    public static YapeNotificationData Parse(YapeNotificationRequest request)
    {
        ValidateEnvelope(request);
        var content = Normalize(string.Join(" ", new[] { request.Title, request.Text, request.BigText }
            .Where(value => !string.IsNullOrWhiteSpace(value))));
        var official = string.Equals(request.PackageName, OfficialPackage, StringComparison.Ordinal);
        var incoming = official && IncomingRegex().IsMatch(content) && !OutgoingRegex().IsMatch(content);
        var amount = incoming ? ReadAmount(content) : null;
        var securityCode = incoming ? ReadSingle(SecurityCodeRegex(), content, "value") : null;
        var payer = incoming ? ReadSingle(PayerRegex(), content, "value") : null;

        var reason = !official
            ? "PAQUETE_NO_PERMITIDO"
            : !incoming
                ? "NO_ES_YAPEO_RECIBIDO"
                : amount is null
                    ? "MONTO_NO_EXTRAIDO"
                    : securityCode is null
                        ? "CODIGO_NO_PRESENTE_EN_NOTIFICACION"
                        : "NOTIFICACION_LISTA_PARA_COMPARAR";

        return new(request.NotificationKey.Trim(), request.DeviceId.Trim(), request.PackageName,
            request.PostedAt, Cut(request.Title, 300), Cut(request.Text, 2000), Cut(request.BigText, 4000),
            incoming, amount, amount.HasValue ? "PEN" : null, securityCode, payer, reason);
    }

    private static void ValidateEnvelope(YapeNotificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NotificationKey) || request.NotificationKey.Length > 200
            || string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 100
            || string.IsNullOrWhiteSpace(request.PackageName) || request.PackageName.Length > 200)
            throw new YapeInputException("NOTIFICACION_INVALIDA", "Faltan identificadores de la notificacion o exceden el limite permitido.");
        if (request.PostedAt < DateTimeOffset.UtcNow.AddDays(-2) || request.PostedAt > DateTimeOffset.UtcNow.AddMinutes(10))
            throw new YapeInputException("FECHA_NOTIFICACION_INVALIDA", "La fecha de la notificacion esta fuera del rango permitido.");
        if ((request.Title?.Length ?? 0) > 300 || (request.Text?.Length ?? 0) > 2000 || (request.BigText?.Length ?? 0) > 4000)
            throw new YapeInputException("CONTENIDO_DEMASIADO_GRANDE", "El contenido de la notificacion excede el limite permitido.");
    }

    private static decimal? ReadAmount(string content)
    {
        var values = AmountRegex().Matches(content).Select(match => match.Groups["value"].Value.Replace(",", ""))
            .Select(value => decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1)
            .Where(value => value > 0 && value <= 10_000m).Distinct().ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static string? ReadSingle(Regex regex, string content, string group)
    {
        var values = regex.Matches(content).Select(match => match.Groups[group].Value.Trim())
            .Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static string Normalize(string value) => WhitespaceRegex().Replace(value, " ").Trim();
    private static string? Cut(string? value, int max) => value?.Length > max ? value[..max] : value;

    [GeneratedRegex(@"\b(recibiste|te\s+yapearon|te\s+yape[oó]|yapeo\s+recibido|te\s+envi[oó]\s+un\s+pago|confirmaci[oó]n\s+de\s+pago)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex IncomingRegex();

    [GeneratedRegex(@"\b(enviaste|yapeaste|pago\s+realizado|yapeo\s+enviado)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex OutgoingRegex();

    [GeneratedRegex(@"(?:S/|S\.)\s*(?<value>(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)(?:\.[0-9]{1,2})?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex AmountRegex();

    [GeneratedRegex(@"\bc[oó]digo(?:\s+de\s+(?:seguridad|operaci[oó]n|yapeo))?\s*[:#-]?\s*(?<value>[0-9]{3})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SecurityCodeRegex();

    [GeneratedRegex(@"(?:\byape!\s+(?<value>[\p{L}][\p{L}\p{M} .'-]{1,100}?)(?=\s+te\s+envi[oó]\s+un\s+pago\b)|\b(?:de|enviado\s+por)\s+(?<value>[\p{L}][\p{L}\p{M} .'-]{1,100}?)(?=\s+(?:por|c[oó]digo|operaci[oó]n|S/|S\.|$)))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PayerRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex WhitespaceRegex();
}
