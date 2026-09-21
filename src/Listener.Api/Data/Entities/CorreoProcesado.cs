namespace Listener.Api.Data.Entities;

public sealed class CorreoProcesado
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool EsSimulacion { get; set; }
    public Guid EstadoSincronizacionCorreoId { get; set; }
    public EstadoSincronizacionCorreo EstadoSincronizacionCorreo { get; set; } = null!;
    public long UidValidity { get; set; }
    public long Uid { get; set; }
    public string? MessageId { get; set; }
    public string? Remitente { get; set; }
    public string? Asunto { get; set; }
    public DateTimeOffset FechaRecepcion { get; set; }
    public DateTimeOffset FechaRegistro { get; set; } = DateTimeOffset.UtcNow;
    public required string HashContenido { get; set; }
    // Reference only. Raw MIME must live in restricted storage with retention rules.
    public string? ReferenciaContenidoProtegido { get; set; }
    public ResultadoAutenticidad ResultadoAutenticidad { get; set; } = ResultadoAutenticidad.NoVerificada;
    public EstadoProcesamientoCorreo EstadoProcesamiento { get; set; } = EstadoProcesamientoCorreo.Pendiente;
    public string? CodigoMotivo { get; set; }
    public string? VersionExtractor { get; set; }
    public string? ResultadoJson { get; set; }
    public byte[] VersionFila { get; set; } = [];
    public ICollection<EvidenciaPago> Evidencias { get; set; } = new List<EvidenciaPago>();
}
