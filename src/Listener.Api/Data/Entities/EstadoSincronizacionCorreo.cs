namespace Listener.Api.Data.Entities;

public sealed class EstadoSincronizacionCorreo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Buzon { get; set; }
    public string Carpeta { get; set; } = "INBOX";
    // IMAP UIDs are unsigned 32-bit values. SQL bigint avoids overflow.
    public long? UidValidity { get; set; }
    public long UltimoUidProcesado { get; set; }
    public DateTimeOffset? UltimaSincronizacionCorrecta { get; set; }
    public byte[] VersionFila { get; set; } = [];
    public ICollection<CorreoProcesado> Correos { get; set; } = new List<CorreoProcesado>();
}
