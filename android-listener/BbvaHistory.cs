using Android.Content;
using AndroidX.Work;
using Java.Util.Concurrent;
using TonysGo.Listener.Bbva;

namespace TonysGo.YapeListener.Android;

internal static class BbvaHistory
{
    private static readonly object Gate = new();
    private static BbvaHistoryStore? _store;
    private static BbvaPendingUploads? _uploads;
    public static BbvaHistoryStore Store(Context context)
    {
        lock (Gate)
            return _store ??= new BbvaHistoryStore(Path.Combine(context.FilesDir!.AbsolutePath, "bbva-history.json"));
    }

    public static BbvaPendingUploads Uploads(Context context)
    {
        lock (Gate)
            return _uploads ??= new BbvaPendingUploads(Path.Combine(context.FilesDir!.AbsolutePath, "bbva-outbox"));
    }

    public static void Enqueue(Context context, BbvaHistoryEntry entry)
    {
        if (!entry.Payment.EsAbono || entry.Status == BbvaDeliveryStatus.StoredByListener) return;
        Uploads(context).Add(entry.Notification);
        Schedule(context, entry.Notification.NotificationKey);
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

    public static void RetryPending(Context context)
    {
        foreach (var entry in Store(context).Read().Where(x => x.Payment.EsAbono && x.Status != BbvaDeliveryStatus.StoredByListener))
        {
            Uploads(context).Add(entry.Notification);
            if (entry.Status != BbvaDeliveryStatus.Sending)
                Store(context).SetStatus(entry.Notification.NotificationKey, BbvaDeliveryStatus.Pending, "Pendiente de envio a Listener.");
        }
        // Older uploads remain pending even after they leave the last-200 history window.
        foreach (var queued in Uploads(context).Read()) Schedule(context, queued.Notification.NotificationKey);
    }
}
