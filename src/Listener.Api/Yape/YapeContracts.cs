namespace Listener.Api.Yape;

public sealed record YapeNotificationRequest(
    string NotificationKey,
    string DeviceId,
    string PackageName,
    DateTimeOffset PostedAt,
    string? Title,
    string? Text,
    string? BigText);

public sealed record YapeNotificationData(
    string NotificationKey,
    string DeviceId,
    string PackageName,
    DateTimeOffset PostedAt,
    string? Title,
    string? Text,
    string? BigText,
    bool EsIngreso,
    decimal? Monto,
    string? Moneda,
    string? CodigoSeguridad,
    string? Pagador,
    string CodigoMotivo);

public sealed record NotificationIngestResult(
    Guid Id,
    bool Duplicada,
    bool EsNotificacionYape,
    bool EsIngreso,
    decimal? Monto,
    string? CodigoSeguridad,
    string CodigoMotivo,
    int SolicitudesResueltas);

public sealed record VerificationCreateRequest(
    string ReferenciaExterna,
    string ReferenciaConductor,
    decimal MontoEsperado,
    string Moneda,
    string MedioPago,
    string ReferenciaCuentaReceptora,
    string CodigoSeguridad);

public sealed record VerificationResult(
    Guid Id,
    string ReferenciaExterna,
    string ReferenciaConductor,
    decimal MontoEsperado,
    string Moneda,
    string MedioPago,
    string CodigoSeguridad,
    string Estado,
    string CodigoMotivo,
    DateTimeOffset FechaCreacion,
    DateTimeOffset FechaExpiracion,
    DateTimeOffset? FechaResolucion,
    Guid? EvidenciaId);

public sealed class YapeInputException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
