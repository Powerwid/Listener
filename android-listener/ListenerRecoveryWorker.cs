using Android.Content;
using Android.OS;
using AndroidX.Work;
using Java.Util.Concurrent;
using WorkResult = AndroidX.Work.ListenableWorker.Result;

namespace TonysGo.YapeListener.Android;

// WorkManager persists this unique watchdog across process death and device restarts.
// Android chooses the actual execution time; this is a fallback, not an exact timer.
public sealed class ListenerRecoveryWorker(Context context, WorkerParameters parameters) : Worker(context, parameters)
{
    private static int _scheduled;
    internal static bool IsUnlocked(Context context) => ((UserManager)context.GetSystemService(Context.UserService)!).IsUserUnlocked;
    internal static void Schedule(Context context)
    {
        if (Interlocked.CompareExchange(ref _scheduled, 1, 0) != 0) return;
        try
        {
            var work = new PeriodicWorkRequest.Builder(Java.Lang.Class.FromType(typeof(ListenerRecoveryWorker)), 15, TimeUnit.Minutes!).Build();
            WorkManager.GetInstance(context).EnqueueUniquePeriodicWork("listener-recovery-v1", ExistingPeriodicWorkPolicy.Keep!, work);
        }
        catch (Exception e)
        {
            Interlocked.Exchange(ref _scheduled, 0);
            ListenerDebugLog.Warning(context, $"No se pudo programar la recuperación: {e.GetType().Name}.");
        }
    }
    public override WorkResult DoWork()
    {
        var context = ApplicationContext!;
        if (!IsUnlocked(context)) return WorkResult.InvokeRetry()!;
        try
        {
            if (NotificationAccess.IsGranted(context))
            {
                if (!ListenerRuntimeService.Running) ListenerRuntimeService.EnsureStarted(context);
                ListenerRuntimeService.RequestRebind(context);
            }
            BbvaHistory.RetryPending(context);
            return WorkResult.InvokeSuccess()!;
        }
        catch (Exception e)
        {
            ListenerDebugLog.Warning(context, $"Recuperación periódica: {e.GetType().Name}.");
            return WorkResult.InvokeRetry()!;
        }
    }
}
