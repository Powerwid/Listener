namespace TonysGo.Listener.Bbva;

public static class BbvaDeliveryRules
{
    public static bool NeedsUpload(BbvaHistoryEntry entry) => entry.Payment.EsAbono && entry.Status != BbvaDeliveryStatus.StoredByListener;

    public static BbvaHistoryEntry Transition(BbvaHistoryEntry entry, BbvaDeliveryStatus status, string detail, DateTimeOffset now)
    {
        if (entry.Status == BbvaDeliveryStatus.StoredByListener) return entry;
        return entry with
        {
            Status = status, Detail = detail,
            Attempts = entry.Attempts + (status == BbvaDeliveryStatus.Sending ? 1 : 0),
            LastAttemptAt = status == BbvaDeliveryStatus.Sending ? now : entry.LastAttemptAt
        };
    }

    public static TimeSpan RetryDelay(int attempts) => TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Clamp(attempts - 1, 0, 6))));
}
