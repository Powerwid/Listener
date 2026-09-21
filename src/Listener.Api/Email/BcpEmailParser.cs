using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MimeKit;
using MimeKit.Text;

namespace Listener.Api.Email;

// Extracted fields are untrusted evidence. This parser never authorizes a recharge.
public sealed record BcpEmailResult
{
    public bool PagoVerificado => false;
    public string AutenticidadCorreo => "NoVerificada";
    public string TipoOperacion { get; init; } = "Desconocida";
    public string Direccion { get; init; } = "Desconocida";
    public string Estado { get; init; } = "RequiereRevision";
    public string Motivo { get; init; } = "FORMATO_NO_RECONOCIDO";
    public string? MessageId { get; init; }
    public string? Remitente { get; init; }
    public string? Asunto { get; init; }
    public decimal? Monto { get; init; }
    public string? Moneda { get; init; }
    public DateTimeOffset? FechaOperacion { get; init; }
    public string? NumeroOperacion { get; init; }
    public string? Ordenante { get; init; }
    public string? Empresa { get; init; }
    public string? ReferenciaCuentaReceptora => null;
}

public static class BcpEmailParser
{
    public const string SenderAddress = "notificaciones@notificacionesbcp.com.pe";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static BcpEmailResult Parse(MimeMessage message)
    {
        var result = new BcpEmailResult
        {
            MessageId = message.MessageId,
            Remitente = message.From.Count == 1 && message.From[0] is MailboxAddress address ? address.Address : null,
            Asunto = message.Subject
        };
        // A matching From is a filter, never authentication. Reject display-name tricks.
        if (message.Headers.Count(h => h.Id == HeaderId.From) != 1
            || message.From.Count != 1 || message.From[0] is not MailboxAddress sender
            || !string.Equals(sender.Address, SenderAddress, StringComparison.OrdinalIgnoreCase))
            return result with { Estado = "Descartado", Motivo = "REMITENTE_NO_PERMITIDO" };

        var text = Normalize(message.HtmlBody is { } html ? HtmlText(html) : message.TextBody ?? "");
        var subject = Normalize(message.Subject ?? "");
        var debit = subject.StartsWith("Realizaste un consumo con tu Tarjeta de Débito BCP", StringComparison.OrdinalIgnoreCase)
            && text.Contains("Operación realizada Consumo Tarjeta de Débito", StringComparison.OrdinalIgnoreCase);
        var yape = subject.StartsWith("Constancia de recepción de Yapeo a celular BCP", StringComparison.OrdinalIgnoreCase)
            && text.Contains("Operación realizada Yapeo a celular", StringComparison.OrdinalIgnoreCase)
            && text.Contains("Recibiste un yapeo de", StringComparison.OrdinalIgnoreCase);

        // Outgoing notices must never enter the incoming-payment matching path.
        if (!debit && !yape)
        {
            if (text.Contains("Pago de servicios", StringComparison.OrdinalIgnoreCase))
                return result with { TipoOperacion = "PagoServicios", Direccion = "Egreso", Estado = "Descartado", Motivo = "OPERACION_NO_CORRESPONDE_A_RECARGA" };
            // A keyword is only a routing hint. Until a real transfer template is
            // supplied, do not guess its direction, amount, or account details.
            if (Regex.IsMatch(subject, @"\btransferencias?\b", Options, RegexTimeout))
                return result with { Motivo = "PLANTILLA_TRANSFERENCIA_PENDIENTE" };
            return result;
        }
        if (debit && yape)
            return result with { Motivo = "CONTENIDO_AMBIGUO" };

        var amountLabel = debit ? "Total del consumo" : "Monto recibido";
        var amount = Amount(text, amountLabel);
        var date = OperationDate(text);
        var operation = Field(text, @"Número de operación\s*:?\s*(?<v>[0-9]{1,50})(?=\s|$)");
        var payer = yape ? Field(text, @"Enviado por\s*:?\s*(?<v>.{1,200}?)\s+¿No reconoces esta operación\?") : null;
        var company = debit ? Field(text, @"Empresa\s*:?\s*(?<v>.{1,200}?)\s+Número de operación") : null;
        result = result with
        {
            TipoOperacion = debit ? "ConsumoTarjetaDebito" : "YapeoRecibido",
            Direccion = debit ? "Egreso" : "Ingreso",
            Monto = amount, Moneda = amount.HasValue ? "PEN" : null,
            FechaOperacion = date, NumeroOperacion = operation, Ordenante = payer, Empresa = company
        };
        if (debit)
            return result with { Estado = "Descartado", Motivo = "OPERACION_NO_CORRESPONDE_A_RECARGA" };

        // Require agreement with the amount repeated in the greeting, where present.
        var greetingAmount = Amount(text, "Recibiste un yapeo de");
        if (amount is null || date is null || payer is null || greetingAmount != amount)
            return result with { Motivo = "DATOS_INCOMPLETOS_O_INCONSISTENTES" };
        return result with { Motivo = "INGRESO_EXTRAIDO_PENDIENTE_AUTENTICIDAD_Y_VINCULACION" };
    }

    private static decimal? Amount(string text, string label)
    {
        var value = Field(text, Regex.Escape(label) + @"\s*:?\s*S/\s*(?<v>(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)\.[0-9]{2})(?=\s|$)");
        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out var amount) && amount > 0 && amount <= 1_000_000m ? amount : null;
    }

    private static DateTimeOffset? OperationDate(string text)
    {
        var value = Field(text, @"Fecha y hora\s*:?\s*(?<v>[0-9]{1,2} de [a-z]+ de [0-9]{4} - [0-9]{1,2}:[0-9]{2} [AP]M)(?=\s|$)");
        if (value is null) return null;
        value = value.Replace("setiembre", "septiembre", StringComparison.OrdinalIgnoreCase);
        var match = Regex.Match(value, @"^(?<day>[0-9]{1,2}) de (?<month>[a-z]+) de (?<year>[0-9]{4}) - (?<hour>[0-9]{1,2}):(?<minute>[0-9]{2}) (?<ampm>[AP]M)$", Options, RegexTimeout);
        var months = new[] { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
        var month = Array.IndexOf(months, match.Groups["month"].Value.ToLowerInvariant()) + 1;
        var hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        if (month == 0 || hour is < 1 or > 12) return null;
        hour = hour % 12 + (match.Groups["ampm"].Value.Equals("PM", StringComparison.OrdinalIgnoreCase) ? 12 : 0);
        try
        {
            // The two supported BCP templates express operation time in Peru (UTC-05).
            return new DateTimeOffset(int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture), month,
                int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture), hour,
                int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture), 0, TimeSpan.FromHours(-5));
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static string? Field(string text, string pattern)
    {
        var matches = Regex.Matches(text, pattern, Options, RegexTimeout);
        return matches.Count == 1 ? matches[0].Groups["v"].Value.Trim() : null;
    }

    private static string Normalize(string text) => Regex.Replace(text.Replace("\u200b", ""), @"\s+", " ", RegexOptions.None, RegexTimeout).Trim();

    private static string HtmlText(string html)
    {
        using var reader = new StringReader(html);
        var tokenizer = new HtmlTokenizer(reader) { DecodeCharacterReferences = true };
        var output = new StringBuilder();
        var ignoredTag = "";
        while (tokenizer.ReadNextToken(out var token))
        {
            if (token is HtmlTagToken tag)
            {
                var name = tag.Name.ToLowerInvariant();
                if (ignoredTag.Length > 0)
                {
                    if (tag.IsEndTag && name == ignoredTag) ignoredTag = "";
                }
                else if (!tag.IsEndTag && name is "style" or "script" or "head") ignoredTag = name;
                output.Append(' ');
            }
            else if (token is HtmlDataToken data && token.Kind == HtmlTokenKind.Data && ignoredTag.Length == 0)
                output.Append(data.Data);
        }
        return output.ToString();
    }
}
