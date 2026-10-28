using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Service.Notification;
using TonysGo.Listener.Bbva;

namespace TonysGo.YapeListener.Android;

[Service(Name = "com.tonysgo.yapelistener.ListenerRuntimeService", Exported = false,
    ForegroundServiceType = ForegroundService.TypeSpecialUse)]
public sealed class ListenerRuntimeService : Service
{
    private static ListenerRuntimeService? _current;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private ConnectivityManager? _connectivity;
    private NetworkObserver? _networkObserver;
    private CancellationTokenSource? _retryDelay;
    private int _pumping, _requested;
    private readonly ListenerRecoveryPlan _recovery = new();
    private UnlockObserver? _unlockObserver;
    private string? _noticeText;
    private Java.Lang.Runnable? _rebindAction;
    public static bool Running => _current is not null;
    public static string? LastError { get; private set; }
    public static bool NeedsHelp => _current?._recovery.NeedsHelp == true && !YapeNotificationListenerService.Connected;

    public static void EnsureStarted(Context context)
    {
        if (!ListenerRecoveryWorker.IsUnlocked(context)) return;
        if (_current is { } current)
        {
            current._handler.Post(current.CheckListener);
            KickUploads();
            return;
        }
        try
        {
            context.StartForegroundService(new Intent(context, typeof(ListenerRuntimeService)));
            LastError = null;
        }
        catch (Exception e)
        {
            LastError = "Android no permitió iniciar el servicio. Abre Listener y revisa el acceso a notificaciones y la batería.";
            ListenerDebugLog.Warning(context, $"Inicio del servicio: {e.GetType().Name}.");
            if (!YapeNotificationListenerService.Connected) RequestRebind(context);
        }
    }

    public override void OnCreate()
    {
        base.OnCreate(); _current = this;
        _rebindAction = new Java.Lang.Runnable(CheckListener);
        var notification = ListenerNotifications.Service(this, "Preparando recepción de notificaciones…");
        if (OperatingSystem.IsAndroidVersionAtLeast(34)) StartForeground(ListenerNotifications.ServiceId, notification, ForegroundService.TypeSpecialUse);
        else StartForeground(ListenerNotifications.ServiceId, notification);
        _connectivity = (ConnectivityManager)GetSystemService(ConnectivityService)!;
        _networkObserver = new NetworkObserver(this);
        _connectivity.RegisterDefaultNetworkCallback(_networkObserver);
        _unlockObserver = new UnlockObserver();
        var unlockFilter = new IntentFilter(Intent.ActionUserUnlocked);
        unlockFilter.AddAction(Intent.ActionUserPresent);
        if (OperatingSystem.IsAndroidVersionAtLeast(33)) RegisterReceiver(_unlockObserver, unlockFilter, ReceiverFlags.NotExported);
        else RegisterReceiver(_unlockObserver, unlockFilter);
        ListenerRecoveryWorker.Schedule(this);
        ListenerDebugLog.Info(this, "Servicio continuo iniciado; comprobando conexión real con Android.");
        _ = Task.Run(() =>
        {
            try { BbvaHistory.RetryPending(this); }
            catch (Exception e) { RecordError(e); }
        });
        CheckListener();
    }
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        CheckListener(); KickUploads(); return StartCommandResult.Sticky;
    }
    public override IBinder? OnBind(Intent? intent) => null;
    public override void OnTaskRemoved(Intent? rootIntent)
    {
        // Removing the activity from Recents does not stop this foreground service.
        CheckListener(); KickUploads(); base.OnTaskRemoved(rootIntent);
    }
    public override void OnDestroy()
    {
        if (ReferenceEquals(_current, this)) _current = null;
        _handler.RemoveCallbacksAndMessages(null);
        _retryDelay?.Cancel();
        if (_networkObserver is not null) _connectivity?.UnregisterNetworkCallback(_networkObserver);
        if (_unlockObserver is not null) UnregisterReceiver(_unlockObserver);
        base.OnDestroy();
    }

    internal static void ListenerChanged()
    {
        var current = _current;
        current?._handler.Post(current.CheckListener);
    }
    internal static void RefreshNotice()
    {
        var current = _current;
        current?._handler.Post(current.UpdateNotice);
    }
    internal static void KickUploads()
    {
        var current = _current;
        if (current is null) return;
        Interlocked.Exchange(ref current._requested, 1);
        if (Interlocked.CompareExchange(ref current._pumping, 1, 0) != 0) return;
        _ = Task.Run(current.PumpAsync);
    }
    private async Task PumpAsync()
    {
        try
        {
            do
            {
                Interlocked.Exchange(ref _requested, 0);
                if (!HasInternet() || !ListenerConfig.Load(this).IsValid) break;
                var pending = BbvaHistory.Store(this).Pending(dueOnly: true);
                if (pending.Count == 0) break;
                var retry = false;
                foreach (var entry in pending)
                {
                    var outcome = await BbvaUploadSender.SendAsync(this, entry.Notification.NotificationKey).ConfigureAwait(false);
                    if (outcome is BbvaUploadOutcome.Retry or BbvaUploadOutcome.ConfigurationRequired) { retry = true; break; }
                }
                if (!retry && pending.Count == 30) Interlocked.Exchange(ref _requested, 1);
                if (retry) break;
            } while (Volatile.Read(ref _requested) != 0 && ReferenceEquals(_current, this));
        }
        catch (Exception e) { RecordError(e); }
        finally
        {
            Interlocked.Exchange(ref _pumping, 0);
            if (Interlocked.Exchange(ref _requested, 0) != 0 && ReferenceEquals(_current, this)) KickUploads();
            ScheduleRetry(); RefreshNotice();
        }
    }
    private void ScheduleRetry()
    {
        _retryDelay?.Cancel();
        if (!ReferenceEquals(_current, this) || !HasInternet() || !ListenerConfig.Load(this).IsValid) return;
        try
        {
            var next = BbvaHistory.Store(this).NextRetry();
            if (next is null) return;
            var delay = next.Value - DateTimeOffset.UtcNow;
            _retryDelay = new CancellationTokenSource(); var token = _retryDelay.Token;
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(delay < TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : delay, token); KickUploads(); }
                catch (System.OperationCanceledException) { }
            });
        }
        catch (Exception e) { RecordError(e); }
    }
    private bool HasInternet()
    {
        var network = _connectivity?.ActiveNetwork;
        using var capabilities = network is null ? null : _connectivity?.GetNetworkCapabilities(network);
        return capabilities?.HasCapability(NetCapability.Validated) == true;
    }

    private void CheckListener()
    {
        if (_rebindAction is not null) _handler.RemoveCallbacks(_rebindAction);
        var step = _recovery.Check(SystemClock.ElapsedRealtime(), YapeNotificationListenerService.Connected,
            NotificationAccess.IsGranted(this), ListenerRecoveryWorker.IsUnlocked(this));
        if (step.RefreshRegistration) RefreshRegistration();
        if (step.RequestBinding) RequestSystemRebind(this);
        _handler.PostDelayed(_rebindAction!, step.CheckAfterMilliseconds);
        UpdateNotice();
    }
    internal static void RequestRebind(Context context)
    {
        if (_current is { } current) current._handler.Post(current.CheckListener);
        else if (ListenerRecoveryWorker.IsUnlocked(context) && !YapeNotificationListenerService.Connected) RequestSystemRebind(context);
    }
    private static ComponentName ListenerComponent(Context context) => new(context.PackageName!, "com.tonysgo.yapelistener.YapeNotificationListenerService");
    private static void RequestSystemRebind(Context context)
    {
        if (!NotificationAccess.IsGranted(context) || YapeNotificationListenerService.Connected) return;
        try { NotificationListenerService.RequestRebind(ListenerComponent(context)); }
        catch (Exception e) { ListenerDebugLog.Warning(context, $"Reconexión pendiente: {e.GetType().Name}."); }
    }
    private void RefreshRegistration()
    {
        // Android 10 can ignore RequestRebind for an already-enabled listener. Ask
        // PackageManager to re-evaluate our registration WITHOUT turning it off,
        // revoking the user's notification grant or terminating the process.
        if (YapeNotificationListenerService.Connected || !NotificationAccess.IsGranted(this)) return;
        try
        {
            var component = ListenerComponent(this);
            var state = PackageManager!.GetComponentEnabledSetting(component);
            if (state is not (ComponentEnabledState.Default or ComponentEnabledState.Enabled)) return;
            PackageManager.SetComponentEnabledSetting(component,
                state == ComponentEnabledState.Default ? ComponentEnabledState.Enabled : ComponentEnabledState.Default,
                ComponentEnableOption.DontKillApp);
            ListenerDebugLog.Info(this, "Registro de lectura actualizado; esperando confirmación de Android.");
        }
        catch (Exception e) { ListenerDebugLog.Warning(this, $"Android no permitió actualizar el registro: {e.GetType().Name}."); }
    }
    private void UpdateNotice()
    {
        try
        {
            var pending = BbvaHistory.Store(this).PendingCount;
            var state = !NotificationAccess.IsGranted(this) ? "Habilita el acceso a notificaciones."
                : NeedsHelp ? "La lectura no está activa. Abre Listener para reparar el acceso."
                : !YapeNotificationListenerService.Connected ? "Reconectando con Android…"
                : !ListenerConfig.Load(this).IsValid ? "Escuchando · teléfono pendiente de vincular."
                : !HasInternet() ? "Escuchando · sin Internet. Los abonos quedan guardados."
                : "Escuchando notificaciones de BBVA Empresas.";
            var text = state + (pending == 0 ? "" : $" {pending} abonos pendientes de envío.");
            if (text == _noticeText) return;
            _noticeText = text;
            using var notification = ListenerNotifications.Service(this, text);
            ((NotificationManager)GetSystemService(NotificationService)!).Notify(ListenerNotifications.ServiceId, notification);
        }
        catch (Exception e) { RecordError(e); }
    }
    private void RecordError(Exception e)
    {
        LastError = $"El servicio encontró un error: {e.GetType().Name}. Revisa el diagnóstico.";
        ListenerDebugLog.Error(this, LastError);
    }
    private sealed class NetworkObserver(ListenerRuntimeService owner) : ConnectivityManager.NetworkCallback
    {
        public override void OnAvailable(Network network) { base.OnAvailable(network); KickUploads(); RefreshNotice(); }
        public override void OnCapabilitiesChanged(Network network, NetworkCapabilities capabilities)
        { base.OnCapabilitiesChanged(network, capabilities); if (capabilities.HasCapability(NetCapability.Validated)) KickUploads(); RefreshNotice(); }
        public override void OnLost(Network network) { base.OnLost(network); owner._retryDelay?.Cancel(); RefreshNotice(); }
    }
    private sealed class UnlockObserver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context is null || !ListenerRecoveryWorker.IsUnlocked(context)) return;
            ListenerChanged(); KickUploads();
        }
    }
}

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced])]
public sealed class ListenerBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action is not (Intent.ActionBootCompleted or Intent.ActionMyPackageReplaced)) return;
        if (!ListenerRecoveryWorker.IsUnlocked(context)) return;
        ListenerRecoveryWorker.Schedule(context);
        if (NotificationAccess.IsGranted(context)) ListenerRuntimeService.EnsureStarted(context);
    }
}
