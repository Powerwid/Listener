using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using System.Globalization;
using TonysGo.Listener.Bbva;

namespace TonysGo.YapeListener.Android;

internal static class ListenerNotifications
{
    public const int ServiceId = 4100;
    private const int CaptureId = 4101;
    private const string ServiceChannel = "listener-running-v1", CaptureChannel = "listener-captured-v1";
    private static readonly object ChannelGate = new();
    private static bool _channelsReady;
    private static NotificationManager Manager(Context context) => (NotificationManager)context.GetSystemService(Context.NotificationService)!;
    public static bool Allowed(Context context) => (!OperatingSystem.IsAndroidVersionAtLeast(33)
        || context.CheckSelfPermission("android.permission.POST_NOTIFICATIONS") == Permission.Granted) && Manager(context).AreNotificationsEnabled();
    public static bool CaptureEnabled(Context context) => Preferences(context).GetBoolean("capture_alerts", true);
    public static void SetCaptureEnabled(Context context, bool enabled) => Preferences(context).Edit()!.PutBoolean("capture_alerts", enabled)!.Apply();
    private static ISharedPreferences Preferences(Context context) => context.GetSharedPreferences("listener_runtime", FileCreationMode.Private)!;

    private static void Channels(Context context)
    {
        lock (ChannelGate)
        {
        if (_channelsReady) return;
        var manager = Manager(context);
        using var service = new NotificationChannel(ServiceChannel, "Escucha continua", NotificationImportance.Low);
        service.Description = "Estado del servicio que recibe los avisos de BBVA Empresas.";
        service.SetShowBadge(false);
        manager.CreateNotificationChannel(service);
        using var captures = new NotificationChannel(CaptureChannel, "Abonos capturados", NotificationImportance.Default);
        captures.Description = "Confirma que Listener guardó el abono en el teléfono.";
        manager.CreateNotificationChannel(captures);
        _channelsReady = true;
        }
    }
    private static PendingIntent Open(Context context) => PendingIntent.GetActivity(context, 0,
        new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop),
        PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

    public static Notification Service(Context context, string detail)
    {
        Channels(context);
        return new Notification.Builder(context, ServiceChannel).SetSmallIcon(Resource.Drawable.ic_listener_status)
            .SetContentTitle(YapeNotificationListenerService.Connected ? "Listener escuchando BBVA"
                : ListenerRuntimeService.NeedsHelp ? "Listener necesita atención" : "Listener · comprobando escucha")!
            .SetContentText(detail)!.SetOngoing(true)!.SetOnlyAlertOnce(true)!.SetShowWhen(false)!
            .SetCategory(Notification.CategoryService)!.SetVisibility(NotificationVisibility.Private)!
            .SetContentIntent(Open(context))!.Build();
    }
    public static void Captured(Context context, BbvaHistoryEntry entry)
    {
        if (!entry.Payment.EsAbono || entry.Status == BbvaDeliveryStatus.StoredByListener || !Allowed(context) || !CaptureEnabled(context)) return;
        var store = BbvaHistory.Store(context);
        if (!store.NeedsAlert(entry.Notification.NotificationKey)) return;
        Publish(context, entry, false);
        store.MarkAlerted(entry.Notification.NotificationKey);
    }
    public static void Delivered(Context context, BbvaHistoryEntry entry)
    {
        if (!Allowed(context) || !CaptureEnabled(context)) return;
        // Do not create an unexpected alert for an old record that had never shown a local notification.
        if (BbvaHistory.Store(context).NeedsAlert(entry.Notification.NotificationKey)) return;
        var manager = Manager(context);
        if (manager.GetActiveNotifications()?.Any(x => x.Tag == entry.Notification.NotificationKey) != true) return;
        Publish(context, entry, true);
    }
    private static void Publish(Context context, BbvaHistoryEntry entry, bool delivered)
    {
        Channels(context);
        var amount = entry.Payment.Monto?.ToString("N2", CultureInfo.GetCultureInfo("es-PE"));
        var body = $"{entry.Payment.NombreCompleto} · S/ {amount}";
        var state = delivered ? "El servidor confirmó la recepción." : "Guardado en el teléfono. Pendiente de envío a la web.";
        using var notification = new Notification.Builder(context, CaptureChannel).SetSmallIcon(Resource.Drawable.ic_listener_status)
            .SetContentTitle(delivered ? "Recibido en la web de Listener" : "BBVA capturado en Listener")!
            .SetContentText(body)!.SetStyle(new Notification.BigTextStyle().BigText(body + "\n" + state))!
            .SetOnlyAlertOnce(true)!.SetAutoCancel(true)!.SetVisibility(NotificationVisibility.Private)!
            .SetContentIntent(Open(context))!.Build();
        Manager(context).Notify(entry.Notification.NotificationKey, CaptureId, notification);
    }
    public static void Test(Context context)
    {
        Channels(context);
        using var notification = new Notification.Builder(context, CaptureChannel).SetSmallIcon(Resource.Drawable.ic_listener_status)
            .SetContentTitle("Prueba de Listener")!.SetContentText("Los avisos locales están habilitados. Esta prueba no registra ningún abono.")!
            .SetAutoCancel(true)!.SetContentIntent(Open(context))!.Build();
        Manager(context).Notify("listener-test", 4102, notification);
    }
}
