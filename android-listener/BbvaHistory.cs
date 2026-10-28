using Android.Content;
using AndroidX.Work;
using Java.Util.Concurrent;
using TonysGo.Listener.Bbva;

namespace TonysGo.YapeListener.Android;

internal static class BbvaHistory
{
    private static readonly object Gate = new();
    private static BbvaLocalDatabase? _store;
    public static BbvaLocalDatabase Store(Context context)
    {
        lock (Gate)
            return _store ??= new BbvaLocalDatabase(context.ApplicationContext!);
    }

    public static void Enqueue(Context context, BbvaHistoryEntry entry)
    {
        if (!BbvaDeliveryRules.NeedsUpload(entry) || entry.Status == BbvaDeliveryStatus.Rejected) return;
        Schedule(context, entry.Notification.NotificationKey);
        ListenerRuntimeService.KickUploads();
    }

    private static void Schedule(Context context, string eventId)
    {
        var input = new Data.Builder()
            .PutString(UploadNotificationWorker.EventIdKey, eventId)
            .PutString(UploadNotificationWorker.PackageKey, BbvaNotificationParser.OfficialPackage).Build();
        var constraints = new Constraints.Builder().SetRequiredNetworkType(NetworkType.Connected!).Build();
        var work = new OneTimeWorkRequest.Builder(Java.Lang.Class.FromType(typeof(UploadNotificationWorker)))
            .SetInputData(input).SetConstraints(constraints)
            .SetBackoffCriteria(BackoffPolicy.Exponential!, 15, TimeUnit.Seconds!).Build();
        WorkManager.GetInstance(context).EnqueueUniqueWork($"bbva-{eventId}", ExistingWorkPolicy.Keep!, work);
    }

    public static void RetryPending(Context context, bool includeRejected = false)
    {
        var configured = ListenerConfig.Load(context).IsValid;
        foreach (var entry in Store(context).Pending(retryRejected: includeRejected))
        {
            if (includeRejected && entry.Status != BbvaDeliveryStatus.Sending || entry.Status == BbvaDeliveryStatus.ConfigurationRequired && configured)
                Store(context).SetStatus(entry.Notification.NotificationKey, BbvaDeliveryStatus.Pending, "Pendiente de envio a Listener.");
            Schedule(context, entry.Notification.NotificationKey);
        }
        ListenerRuntimeService.KickUploads();
    }
}
