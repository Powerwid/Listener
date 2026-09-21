namespace Listener.Api.Data.Entities;

public sealed class EvidenciaPago
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool EsSimulacion { get; set; }
    public string? ReferenciaSolicitudExtraida { get; set; }
    public Guid CorreoProcesadoId { get; set; }
    public CorreoProcesado CorreoProcesado { get; set; } = null!;
    // Nullable until extraction succeeds: missing values are not invented.
    public string? ProveedorPago { get; set; }
    public string? NumeroOperacion { get; set; }
    public string? ReferenciaCuentaReceptora { get; set; }
    public decimal? Monto { get; set; }
    public string? Moneda { get; set; }
    public DateTimeOffset? FechaOperacion { get; set; }
    public string? ReferenciaPagador { get; set; }
    public DireccionMovimiento DireccionMovimiento { get; set; } = DireccionMovimiento.Desconocida;
    public EstadoPagoInformado EstadoPagoInformado { get; set; } = EstadoPagoInformado.Desconocido;
    public EstadoEvidencia EstadoValidacion { get; set; } = EstadoEvidencia.Pendiente;
    public string? CodigoMotivo { get; set; }
    public Guid? SolicitudVerificacionId { get; set; }
    public SolicitudVerificacion? SolicitudVerificacion { get; set; }
    public DateTimeOffset FechaRegistro { get; set; } = DateTimeOffset.UtcNow;
    public byte[] VersionFila { get; set; } = [];
}
