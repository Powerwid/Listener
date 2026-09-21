using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Listener.Api.Simulation;

public sealed class SimulationInputException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record SimulationRequest(
    string ReferenciaExterna,
    string ReferenciaConductor,
    decimal MontoEsperado,
    string Moneda,
    string MedioPago,
    string ReferenciaCuentaReceptora,
    string OperacionDeclarada)
{
    public static SimulationRequest Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || document.RootElement.EnumerateObject().GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new SimulationInputException("JSON_INVALIDO", "La solicitud debe ser un objeto JSON sin campos duplicados.");

        var request = JsonSerializer.Deserialize<SimulationRequest>(json, SimulationJson.Options)
            ?? throw new SimulationInputException("JSON_INVALIDO", "Falta la solicitud.");
        request.Validate();
        return request;
    }

    public void Validate()
    {
        DemoValidation.Reference(ReferenciaExterna, "referenciaExterna");
        DemoValidation.Reference(ReferenciaConductor, "referenciaConductor");
        DemoValidation.Reference(ReferenciaCuentaReceptora, "referenciaCuentaReceptora");
        DemoValidation.Reference(OperacionDeclarada, "operacionDeclarada");
        DemoValidation.Amount(MontoEsperado);
        DemoValidation.Currency(Moneda);
        DemoValidation.Provider(MedioPago);
        if (!ReferenciaExterna.StartsWith("DEMO-", StringComparison.Ordinal))
            throw new SimulationInputException("REFERENCIA_INVALIDA", "Usa una referenciaExterna que empiece con DEMO- para separar las pruebas.");
    }
}

public sealed record DemoMailData(
    string ReferenciaSolicitud, string ReferenciaConductor, decimal Monto, string Moneda,
    string Proveedor, string CuentaReceptora, string NumeroOperacion, DateTimeOffset FechaOperacion,
    string Direccion, string EstadoPago);

public sealed record SimulationResult(
    Guid SolicitudId, string ReferenciaExterna, bool CoincidenciaDatos,
    string[] Diferencias, Guid CorreoId, Guid EvidenciaId, DemoMailData DatosExtraidos)
{
    public bool EsSimulacion => true;
    public bool PagoVerificado => false;
    public string Estado => "RequiereRevision";
    public string AutenticidadCorreo => "NoVerificada";
    public string Motivo => CoincidenciaDatos ? "SIMULACION_COINCIDE" : "SIMULACION_NO_COINCIDE";
}

public static class SimulationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };
}

internal static partial class DemoValidation
{
    // Deliberately limited fixture identifiers; not a real bank identifier policy.
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    public static void Reference(string? value, string field)
    {
        if (value is null || !IdentifierPattern().IsMatch(value))
            throw new SimulationInputException("CAMPO_INVALIDO", $"{field}: usa de 1 a 100 letras, numeros, puntos, guiones o guiones bajos, sin espacios.");
    }

    public static void Amount(decimal amount)
    {
        if (amount <= 0 || amount > 1_000_000m || decimal.Round(amount, 2) != amount)
            throw new SimulationInputException("MONTO_INVALIDO", "El monto de prueba debe ser positivo, tener hasta dos decimales y no superar 1000000.");
    }

    public static void Currency(string? currency)
    {
        if (currency is not ("PEN" or "USD"))
            throw new SimulationInputException("MONEDA_INVALIDA", "La simulacion acepta PEN o USD.");
    }

    public static void Provider(string? provider)
    {
        if (provider is not ("BCP" or "YAPE"))
            throw new SimulationInputException("PROVEEDOR_INVALIDO", "Usa BCP o YAPE como etiqueta de prueba.");
    }
}
