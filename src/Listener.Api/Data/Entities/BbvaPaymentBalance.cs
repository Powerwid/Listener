namespace Listener.Api.Data.Entities;

public sealed class BbvaPaymentBalance
{
    public required string PayerKey { get; set; }
    public decimal PendingAmount { get; set; }
    public Guid GroupId { get; set; } = Guid.NewGuid();
    public long LastSequence { get; set; }
    public byte[] VersionFila { get; set; } = [];
}

public sealed class BbvaPaymentValidation
{
    public Guid EventId { get; set; }
    public required string PayerKey { get; set; }
    public required string FullName { get; set; }
    public Guid GroupId { get; set; }
    public long Sequence { get; set; }
    public decimal ReceivedAmount { get; set; }
    public decimal AccumulatedAmount { get; set; }
    public bool Correct { get; set; }
    public DateTimeOffset NotificationAt { get; set; }
}
