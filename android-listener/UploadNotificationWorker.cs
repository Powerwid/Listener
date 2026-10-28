using Android.Content;
using AndroidX.Work;
using TonysGo.Listener.Bbva;
using WorkResult = AndroidX.Work.ListenableWorker.Result;

namespace TonysGo.YapeListener.Android;

// Durable WorkManager fallback. The foreground service uses the same sender for immediate delivery.
public sealed class UploadNotificationWorker(Context context, WorkerParameters parameters) : Worker(context, parameters)
{
    public const string EventIdKey = "eventId", PackageKey = "packageName";
    public override WorkResult DoWork()
    {
        if (InputData!.GetString(PackageKey) != BbvaNotificationParser.OfficialPackage) return WorkResult.InvokeFailure()!;
        var context = ApplicationContext!;
        try
        {
            var outcome = BbvaUploadSender.SendAsync(context, InputData.GetString(EventIdKey) ?? "").GetAwaiter().GetResult();
            return outcome == BbvaUploadOutcome.Retry ? WorkResult.InvokeRetry()! : WorkResult.InvokeSuccess()!;
        }
        catch (Exception e)
        {
            ListenerDebugLog.Error(context, $"Envío pendiente: {e.GetType().Name}.");
            return WorkResult.InvokeRetry()!;
        }
    }
}
