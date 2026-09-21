using System.Globalization;
using MimeKit;

namespace Listener.Api.Simulation;

/// <summary>Only our synthetic fixture format. Never a BCP/Yape authenticity check.</summary>
public static class DemoEmailParser
{
    private static readonly string[] Fields =
    [
        "REFERENCIA_SOLICITUD", "REFERENCIA_CONDUCTOR", "PROVEEDOR", "NUMERO_OPERACION",
        "CUENTA_RECEPTORA", "MONTO", "MONEDA", "FECHA_OPERACION", "DIRECCION", "ESTADO"
    ];

    public static DemoMailData Parse(MimeMessage message)
    {
        if (message.Headers.Count(h => h.Field.Equals("X-Listener-Demo", StringComparison.OrdinalIgnoreCase)) != 1
            || message.Headers["X-Listener-Demo"] != "v1")
            throw new SimulationInputException("FORMATO_NO_SOPORTADO", "Solo se acepta el correo ficticio X-Listener-Demo: v1. No es un lector de formatos bancarios reales.");

        if (message.Body is not TextPart { IsPlain: true } textPart)
            throw new SimulationInputException("FORMATO_NO_SOPORTADO", "El correo demo debe contener un solo cuerpo text/plain, sin HTML ni adjuntos.");

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in textPart.Text.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
                throw new SimulationInputException("CORREO_INVALIDO", "Cada linea del cuerpo debe tener CAMPO: valor.");
            var key = line[..separator].Trim();
            if (!Fields.Contains(key) || !fields.TryAdd(key, line[(separator + 1)..].Trim()))
                throw new SimulationInputException("CORREO_AMBIGUO", "El correo demo contiene un campo desconocido o repetido.");
        }
        if (Fields.Any(field => !fields.ContainsKey(field)))
            throw new SimulationInputException("CORREO_INCOMPLETO", "Faltan campos del formato demo; compara el archivo con el ejemplo.");

        foreach (var name in new[] { "REFERENCIA_SOLICITUD", "REFERENCIA_CONDUCTOR", "NUMERO_OPERACION", "CUENTA_RECEPTORA" })
            DemoValidation.Reference(fields[name], name);
        DemoValidation.Provider(fields["PROVEEDOR"]);
        DemoValidation.Currency(fields["MONEDA"]);
        if (!decimal.TryParse(fields["MONTO"], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            throw new SimulationInputException("MONTO_INVALIDO", "Usa un monto con punto decimal, sin simbolos ni separadores de miles.");
        DemoValidation.Amount(amount);
        if (!DateTimeOffset.TryParseExact(fields["FECHA_OPERACION"], "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
            throw new SimulationInputException("FECHA_INVALIDA", "Usa una fecha UTC con formato 2026-09-04T12:00:00Z.");
        if (fields["DIRECCION"] is not ("INGRESO" or "SALIDA")
            || fields["ESTADO"] is not ("COMPLETADO" or "PENDIENTE" or "FALLIDO" or "REVERSADO"))
            throw new SimulationInputException("ESTADO_INVALIDO", "Revisa DIRECCION y ESTADO del correo demo.");

        return new(fields["REFERENCIA_SOLICITUD"], fields["REFERENCIA_CONDUCTOR"], amount,
            fields["MONEDA"], fields["PROVEEDOR"], fields["CUENTA_RECEPTORA"], fields["NUMERO_OPERACION"],
            date, fields["DIRECCION"], fields["ESTADO"]);
    }

    public static string[] Compare(SimulationRequest request, DemoMailData mail)
    {
        var differences = new List<string>();
        if (request.ReferenciaExterna != mail.ReferenciaSolicitud) differences.Add("REFERENCIA_SOLICITUD");
        if (request.ReferenciaConductor != mail.ReferenciaConductor) differences.Add("REFERENCIA_CONDUCTOR");
        if (request.MontoEsperado != mail.Monto) differences.Add("MONTO");
        if (request.Moneda != mail.Moneda) differences.Add("MONEDA");
        if (request.MedioPago != mail.Proveedor) differences.Add("PROVEEDOR");
        if (request.ReferenciaCuentaReceptora != mail.CuentaReceptora) differences.Add("CUENTA_RECEPTORA");
        if (request.OperacionDeclarada != mail.NumeroOperacion) differences.Add("NUMERO_OPERACION");
        if (mail.Direccion != "INGRESO") differences.Add("NO_ES_INGRESO");
        if (mail.EstadoPago != "COMPLETADO") differences.Add("PAGO_NO_COMPLETADO");
        return differences.ToArray();
    }
}
