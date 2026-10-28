using TonysGo.Listener.Bbva;

namespace Listener.Tests;

public class BbvaDeliveryRulesTests
{
    [Fact]
    public void CapturedAbonoRemainsPendingUntilPositiveServerReceipt()
    {
        var notification = BbvaNotificationTests.Notice();
        var entry = new BbvaHistoryEntry(notification, BbvaNotificationParser.Parse(notification), DateTimeOffset.UtcNow, BbvaDeliveryStatus.Pending);
        Assert.True(BbvaDeliveryRules.NeedsUpload(entry));
        var now = DateTimeOffset.UtcNow;
        var sending = BbvaDeliveryRules.Transition(entry, BbvaDeliveryStatus.Sending, "Enviando", now);
        Assert.Equal(1, sending.Attempts);
        Assert.Equal(now, sending.LastAttemptAt);
        var retry = BbvaDeliveryRules.Transition(sending, BbvaDeliveryStatus.Retry, "Sin Internet", now);
        Assert.True(BbvaDeliveryRules.NeedsUpload(retry));
        Assert.Equal(1, retry.Attempts);
        var sent = BbvaDeliveryRules.Transition(retry, BbvaDeliveryStatus.StoredByListener, "Recibido", now);
        Assert.False(BbvaDeliveryRules.NeedsUpload(sent));
        Assert.Equal(sent, BbvaDeliveryRules.Transition(sent, BbvaDeliveryStatus.Retry, "Respuesta atrasada", now));
    }

    [Fact]
    public void RepeatedFailuresBackOffWithoutAnUnboundedDelayOrBusyLoop()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), BbvaDeliveryRules.RetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(10), BbvaDeliveryRules.RetryDelay(2));
        Assert.Equal(TimeSpan.FromMinutes(5), BbvaDeliveryRules.RetryDelay(100));
    }
}
