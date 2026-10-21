using TonysGo.Listener.Bbva;

namespace Listener.Tests;

public sealed class BbvaHistoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"listener-bbva-tests-{Guid.NewGuid():N}");
    private string HistoryPath => Path.Combine(_directory, "history.json");

    [Fact]
    public void HistorySurvivesRestartAndDuplicateCapturePreservesDelivery()
    {
        var store = new BbvaHistoryStore(HistoryPath);
        var input = BbvaNotificationTests.Notice();
        store.Capture(input);
        store.SetStatus(input.NotificationKey, BbvaDeliveryStatus.Sending, "Enviando");
        store.SetStatus(input.NotificationKey, BbvaDeliveryStatus.StoredByListener, "HTTP 200");
        var reopened = new BbvaHistoryStore(HistoryPath);
        var entry = reopened.Capture(input);
        Assert.Single(reopened.Read());
        Assert.Equal(BbvaDeliveryStatus.StoredByListener, entry.Status);
        Assert.Equal(1, entry.Attempts);
        Assert.Equal(input.Text, entry.Notification.Text);
        reopened.SetStatus(input.NotificationKey, BbvaDeliveryStatus.Retry, "A late error cannot downgrade delivery");
        Assert.Equal(BbvaDeliveryStatus.StoredByListener, Assert.Single(reopened.Read()).Status);
    }

    [Fact]
    public void InterruptedUploadBecomesRetryableAfterRestart()
    {
        var store = new BbvaHistoryStore(HistoryPath);
        var input = BbvaNotificationTests.Notice();
        store.Capture(input);
        store.SetStatus(input.NotificationKey, BbvaDeliveryStatus.Sending, "Enviando");
        Assert.Equal(BbvaDeliveryStatus.Retry, Assert.Single(new BbvaHistoryStore(HistoryPath).Read()).Status);
    }

    [Fact]
    public void BbvaNonPaymentsStayInHistoryAndCanBecomeCompleteOnExpansion()
    {
        var store = new BbvaHistoryStore(HistoryPath);
        var input = BbvaNotificationTests.Notice();
        Assert.Equal(BbvaDeliveryStatus.HistoryOnly, store.Capture(input with { Text = "Mensaje truncado…" }).Status);
        Assert.Equal(BbvaDeliveryStatus.Pending, store.Capture(input).Status);
        Assert.Single(store.Read());
        var other = input with { NotificationKey = "otp", Title = "Token digital", Text = "Tu código es 123456" };
        Assert.Equal(BbvaDeliveryStatus.HistoryOnly, store.Capture(other).Status);
        Assert.Throws<ArgumentException>(() => store.Capture(input with { PackageName = "com.example.other" }));
    }

    [Fact]
    public void HistoryKeepsTheLatestTwoHundredNoticesInOrder()
    {
        var store = new BbvaHistoryStore(HistoryPath);
        var input = BbvaNotificationTests.Notice();
        for (var index = 0; index < 205; index++)
            store.Capture(input with { NotificationKey = $"event-{index}", PostedAt = input.PostedAt.AddSeconds(index) });
        Assert.Equal(BbvaHistoryStore.MaximumEntries, store.Read().Count);
        Assert.Equal("event-204", store.Read()[0].Notification.NotificationKey);
        Assert.DoesNotContain(store.Read(), x => x.Notification.NotificationKey == "event-0");
    }

    [Fact]
    public void UploadOutboxRetainsLongMessagesAndBindsTheDeviceAcrossRetries()
    {
        // A queue reference stays tiny even if Android's expanded message exceeds WorkManager's data limit.
        var queue = new BbvaPendingUploads(_directory);
        var input = BbvaNotificationTests.Notice() with { BigText = new string('Á', 4000) };
        queue.Add(input);
        queue.BindDevice(input.NotificationKey, "original-phone");
        var reopened = new BbvaPendingUploads(_directory);
        reopened.Add(input with { Text = "A repeated capture must not replace the queued message" });
        var queued = reopened.BindDevice(input.NotificationKey, "renamed-phone");
        Assert.Equal("original-phone", queued.UploadDeviceId);
        Assert.Equal(input, queued.Notification);
        Assert.Single(reopened.Read());
        reopened.Remove(input.NotificationKey);
        Assert.Empty(reopened.Read());
    }

    [Fact]
    public void OutboxRejectsAnEventIdThatCouldEscapePrivateStorage()
    {
        var queue = new BbvaPendingUploads(_directory);
        Assert.Throws<ArgumentException>(() => queue.Add(BbvaNotificationTests.Notice() with { NotificationKey = "../outside" }));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_directory)) return;
        foreach (var file in Directory.EnumerateFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory);
    }
}
