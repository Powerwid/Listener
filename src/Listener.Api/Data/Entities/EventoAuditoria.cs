namespace Listener.Api.Data.Entities;

public sealed class EventoAuditoria
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SolicitudVerificacionId { get; set; }
    public SolicitudVerificacion? SolicitudVerificacion { get; set; }
    public Guid? CorreoProcesadoId { get; set; }
    public CorreoProcesado? CorreoProcesado { get; set; }
    public required string TipoEvento { get; set; }
    public required string Actor { get; set; }
    public DateTimeOffset Fecha { get; set; } = DateTimeOffset.UtcNow;
    public string? EstadoAnterior { get; set; }
    public string? EstadoNuevo { get; set; }
    public required string CodigoMotivo { get; set; }
    public required string IdCorrelacion { get; set; }
}
