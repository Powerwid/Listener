namespace Listener.Api.Email;

public sealed record EmailReadItem(string Referencia, BcpEmailResult? Datos = null, string? Error = null);

/// <summary>The same extraction report is used for file batches and a Gmail read.</summary>
public sealed record EmailReadReport
{
    public string Origen { get; init; } = "ArchivosLocales";
    public string? Buzon { get; init; }
    public string? Carpeta { get; init; }
    public uint? UidValidity { get; init; }
    public string? Desde { get; init; }
    public int CoincidenciasFiltro { get; init; }
    public int Limite { get; init; } = 20;
    public bool Persistido { get; init; }
    public bool PagoVerificado => false;
    public required IReadOnlyList<EmailReadItem> IngresosExtraidos { get; init; }
    public required IReadOnlyList<EmailReadItem> Descartados { get; init; }
    public required IReadOnlyList<EmailReadItem> Revision { get; init; }
    public required IReadOnlyList<EmailReadItem> Errores { get; init; }
    public object Resumen => new
    {
        procesados = IngresosExtraidos.Count + Descartados.Count + Revision.Count + Errores.Count,
        ingresosExtraidos = IngresosExtraidos.Count,
        descartados = Descartados.Count,
        revision = Revision.Count,
        errores = Errores.Count
    };

    public static EmailReadReport Create(IReadOnlyList<EmailReadItem> items) => new()
    {
        CoincidenciasFiltro = items.Count,
        // Extraction means recognizable fields, not verified funds or a matched driver.
        IngresosExtraidos = items.Where(IsExtracted).ToArray(),
        Descartados = items.Where(i => i.Error is null && i.Datos?.Estado == "Descartado").ToArray(),
        Revision = items.Where(i => i.Error is null && i.Datos is not null && i.Datos.Estado != "Descartado" && !IsExtracted(i)).ToArray(),
        Errores = items.Where(i => i.Error is not null || i.Datos is null).ToArray()
    };

    private static bool IsExtracted(EmailReadItem item) => item.Error is null
        && item.Datos is { Direccion: "Ingreso", Motivo: "INGRESO_EXTRAIDO_PENDIENTE_AUTENTICIDAD_Y_VINCULACION" };
}
