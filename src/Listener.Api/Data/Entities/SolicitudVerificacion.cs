namespace Listener.Api.Data.Entities;

public sealed class SolicitudVerificacion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool EsSimulacion { get; set; }
    public required string ReferenciaExterna { get; set; }
    // Opaque TonysGo reference; no FK or access to its conductor tables.
    public required string ReferenciaConductor { get; set; }
    public decimal MontoEsperado { get; set; }
    public string Moneda { get; set; } = "PEN";
    public required string MedioPago { get; set; }
    public required string ReferenciaCuentaReceptora { get; set; }
    public string? OperacionDeclarada { get; set; }
    public EstadoSolicitud Estado { get; set; } = EstadoSolicitud.Pendiente;
    public string? CodigoMotivo { get; set; }
    public DateTimeOffset FechaCreacion { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset FechaExpiracion { get; set; }
    public DateTimeOffset? FechaResolucion { get; set; }
    public byte[] VersionFila { get; set; } = [];
    public EvidenciaPago? EvidenciaAsignada { get; set; }
}
