using System.Security.Cryptography;
using System.Text;
using Android.App;
using Android.Content;
using Android.Service.Notification;
using AndroidX.Work;
using Java.Util.Concurrent;

namespace TonysGo.YapeListener.Android;

[Service(
    Name = "com.tonysgo.yapelistener.YapeNotificationListenerService",
    Label = "Tony's Go Yape Listener",
    Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
    Exported = true)]
[IntentFilter(["android.service.notification.NotificationListenerService"])]
public sealed class YapeNotificationListenerService : NotificationListenerService
{
    public const string YapePackage = "com.bcp.innovacxion.yapeapp";

    public override void OnListenerConnected()
    {
        base.OnListenerConnected();
        var activeNotifications = GetActiveNotifications() ?? [];
        ListenerDebugLog.Info(this, $"Listener conectado. Notificaciones activas detectadas: {activeNotifications.Length}.");
        foreach (var notification in activeNotifications)
        {
            ProcessNotification(notification);
        }
    }

    public override void OnNotificationPosted(StatusBarNotification? notification)
    {
        base.OnNotificationPosted(notification);
        ListenerDebugLog.Info(this,
            $"Intento de intercepcion. Paquete={notification?.PackageName ?? "desconocido"}; fechaUnix={notification?.PostTime ?? 0}.");
        ProcessNotification(notification);
    }

    private void ProcessNotification(StatusBarNotification? notification)
    {
        if (notification is null)
        {
            ListenerDebugLog.Warning(this, "Notificacion ignorada: Android entrego un evento vacio.");
            return;
        }
        if (notification.PackageName != YapePackage)
        {
            ListenerDebugLog.Info(this, $"Notificacion ignorada: paquete no permitido ({notification.PackageName}).");
            return;
        }
        if (!ListenerConfig.Load(this).IsValid)
        {
            ListenerDebugLog.Warning(this, "Notificacion Yape detectada, pero la configuracion local no es valida.");
            return;
        }

        var extras = notification.Notification?.Extras;
        var title = extras?.GetCharSequence("android.title")?.ToString() ?? string.Empty;
        var text = extras?.GetCharSequence("android.text")?.ToString() ?? string.Empty;
        var bigText = extras?.GetCharSequence("android.bigText")?.ToString() ?? string.Empty;
        var subText = extras?.GetCharSequence("android.subText")?.ToString() ?? string.Empty;
        var infoText = extras?.GetCharSequence("android.infoText")?.ToString() ?? string.Empty;
        var tickerText = notification.Notification?.TickerText?.ToString() ?? string.Empty;
        bigText = string.Join(" ", new[] { bigText, subText, infoText, tickerText }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal));

        if (string.IsNullOrWhiteSpace(title) &&
            string.IsNullOrWhiteSpace(text) &&
            string.IsNullOrWhiteSpace(bigText))
        {
            ListenerDebugLog.Warning(this, "Notificacion Yape ignorada: no contiene texto util en los extras de Android.");
            return;
        }

        var eventId = Sha256($"{notification.Key}|{notification.PostTime}|{title}|{text}|{bigText}");
        ListenerDebugLog.Info(this,
            $"Notificacion Yape capturada. Evento={eventId[..12]}; tituloChars={title.Length}; textoChars={text.Length}; detalleChars={bigText.Length}.");
        ListenerConfig.SetLastStatus(this,
            $"Notificacion Yape capturada a las {DateTimeOffset.Now:HH:mm:ss}. Pendiente de envio.");

        var input = new Data.Builder()
            .PutString(UploadNotificationWorker.EventIdKey, eventId)
            .PutLong(UploadNotificationWorker.PostedAtKey, notification.PostTime)
            .PutString(UploadNotificationWorker.TitleKey, title)
            .PutString(UploadNotificationWorker.TextKey, text)
            .PutString(UploadNotificationWorker.BigTextKey, bigText)
            .Build();

        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)
            .Build();

        var request = new OneTimeWorkRequest.Builder(
                Java.Lang.Class.FromType(typeof(UploadNotificationWorker)))
            .SetInputData(input)
            .SetConstraints(constraints)
            .SetBackoffCriteria(BackoffPolicy.Exponential!, 15, TimeUnit.Seconds!)
            .Build();

        WorkManager.GetInstance(this)
            .EnqueueUniqueWork($"yape-{eventId}", ExistingWorkPolicy.Keep!, request);
        ListenerDebugLog.Info(this, $"Envio encolado en WorkManager. Evento={eventId[..12]}.");
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
