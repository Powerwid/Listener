using Android.App;
using Android.Service.Notification;
using System.Threading.Channels;
using TonysGo.Listener.Bbva;

namespace TonysGo.YapeListener.Android;

// Preserve the component identity and existing notification-access grants during upgrades.
[Service(
    Name = "com.tonysgo.yapelistener.YapeNotificationListenerService",
    Label = "Tony's Go BBVA Listener",
    Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
    Enabled = true,
    Exported = true)]
[IntentFilter(["android.service.notification.NotificationListenerService"])]
public sealed class YapeNotificationListenerService : NotificationListenerService
{
    private static readonly Channel<BbvaNotificationRequest> Captures = Channel.CreateUnbounded<BbvaNotificationRequest>(new() { SingleReader = true });
    private static int _consumerStarted;
    public static bool Connected { get; private set; }

    public override void OnListenerConnected()
    {
        base.OnListenerConnected();
        Connected = true;
        // A system rebind can arrive before (or instead of) the boot broadcast.
        if (!ListenerRuntimeService.Running) ListenerRuntimeService.EnsureStarted(this);
        ListenerRuntimeService.ListenerChanged();
        ListenerDebugLog.Info(this, "Listener BBVA conectado.");
        try { foreach (var notification in GetActiveNotifications() ?? []) ProcessNotification(notification); }
        catch (Exception e) { ListenerDebugLog.Warning(this, $"Recuperación de avisos activos: {e.GetType().Name}."); }
        _ = Task.Run(() =>
        {
            try { BbvaHistory.RetryPending(ApplicationContext!); }
            catch (Exception e) { ListenerDebugLog.Error(ApplicationContext!, $"Recuperación de cola: {e.GetType().Name}."); }
        });
    }

    public override void OnListenerDisconnected()
    {
        base.OnListenerDisconnected(); Connected = false;
        ListenerDebugLog.Warning(this, "Android desconectó la escucha. Solicitando reconexión.");
        ListenerRuntimeService.RequestRebind(this);
        ListenerRuntimeService.ListenerChanged();
    }
    public override void OnDestroy()
    {
        Connected = false;
        ListenerRuntimeService.ListenerChanged();
        base.OnDestroy();
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
            // Snapshot Android's message now, then commit off the callback thread in arrival order.
            StartConsumer();
            Captures.Writer.TryWrite(request);
        }
        catch (Exception exception)
        {
            ListenerDebugLog.Error(this, $"No se pudo guardar el aviso BBVA: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void StartConsumer()
    {
        if (Interlocked.CompareExchange(ref _consumerStarted, 1, 0) != 0) return;
        var context = ApplicationContext!;
        _ = Task.Run(async () =>
        {
            await foreach (var request in Captures.Reader.ReadAllAsync())
            {
                try
                {
                    var capture = BbvaHistory.Store(context).Capture(request);
                    // SQLite commits the history and pending state before any alert or network request.
                    ListenerConfig.SetLastStatus(context, $"Aviso BBVA guardado a las {DateTimeOffset.Now:HH:mm:ss}.");
                    try { ListenerNotifications.Captured(context, capture.Entry); }
                    catch (Exception e) { ListenerDebugLog.Warning(context, $"Aviso local: {e.GetType().Name}."); }
                    BbvaHistory.Enqueue(context, capture.Entry);
                    ListenerRuntimeService.RefreshNotice();
                }
                catch (Exception e) { ListenerDebugLog.Error(context, $"No se pudo guardar un aviso BBVA: {e.GetType().Name}."); }
            }
        });
    }

    private static string? Cut(string? value, int maximum) => value?.Length > maximum ? value[..maximum] : value;
}
