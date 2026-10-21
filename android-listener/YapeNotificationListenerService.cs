using Android.App;
using Android.Service.Notification;
using TonysGo.Listener.Bbva;

namespace TonysGo.YapeListener.Android;

// Preserve the component identity and existing notification-access grants during upgrades.
[Service(
    Name = "com.tonysgo.yapelistener.YapeNotificationListenerService",
    Label = "Tony's Go BBVA Listener",
    Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
    Exported = true)]
[IntentFilter(["android.service.notification.NotificationListenerService"])]
public sealed class YapeNotificationListenerService : NotificationListenerService
{
    public override void OnListenerConnected()
    {
        base.OnListenerConnected();
        ListenerDebugLog.Info(this, "Listener BBVA conectado.");
        foreach (var notification in GetActiveNotifications() ?? []) ProcessNotification(notification);
        BbvaHistory.RetryPending(this);
    }

    public override void OnNotificationPosted(StatusBarNotification? notification)
    {
        base.OnNotificationPosted(notification);
        ProcessNotification(notification);
    }

    private void ProcessNotification(StatusBarNotification? notification)
    {
        // Do not log or retain notifications from other applications.
        if (notification?.PackageName != BbvaNotificationParser.OfficialPackage) return;
        try
        {
            var extras = notification.Notification?.Extras;
            var title = extras?.GetCharSequence("android.title")?.ToString();
            var text = extras?.GetCharSequence("android.text")?.ToString();
            var bigText = extras?.GetCharSequence("android.bigText")?.ToString();
            if (string.IsNullOrWhiteSpace(bigText))
                bigText = string.Join("\n", extras?.GetCharSequenceArray("android.textLines")?.Select(x => x?.ToString()) ?? []);
            if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(bigText))
                bigText = notification.Notification?.TickerText?.ToString();

            var eventId = BbvaNotificationParser.EventId(notification.Key ?? notification.Id.ToString(), notification.PostTime);
            var request = new BbvaNotificationRequest(eventId, ListenerConfig.Load(this).DeviceId,
                notification.PackageName, DateTimeOffset.FromUnixTimeMilliseconds(notification.PostTime),
                Cut(title, 300), Cut(text, 2000), Cut(bigText, 4000));
            // Keep the local record even when configuration is not ready or this isn't a payment.
            var entry = BbvaHistory.Store(this).Capture(request);
            ListenerConfig.SetLastStatus(this, $"Aviso BBVA capturado a las {DateTimeOffset.Now:HH:mm:ss}.");
            ListenerDebugLog.Info(this, $"Aviso BBVA {eventId[..12]}: {entry.Payment.CodigoMotivo}.");
            BbvaHistory.Enqueue(this, entry);
        }
        catch (Exception exception)
        {
            ListenerDebugLog.Error(this, $"No se pudo guardar el aviso BBVA: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string? Cut(string? value, int maximum) => value?.Length > maximum ? value[..maximum] : value;
}