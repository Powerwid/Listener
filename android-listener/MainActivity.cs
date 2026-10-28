using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using System.Globalization;
using System.Runtime.Versioning;
using TonysGo.Listener.Bbva;
using TonysGo.Listener.Pairing;

namespace TonysGo.YapeListener.Android;

[Activity(Label = "Tony's Go BBVA Listener", Icon = "@mipmap/ic_launcher", MainLauncher = true, Exported = true,
    Theme = "@style/ListenerTheme")]
public sealed partial class MainActivity : Activity
{
    private Button? _linkButton;
    private TextView? _status, _connectionResult, _debugLog, _summary, _serviceState;
    private View? _historyPane, _configurationPane, _servicePane;
    private BbvaHistoryAdapter? _adapter;
    private long _historyRevision = -1;
    private CancellationTokenSource? _refreshCancellation;
    private static readonly HttpClient ConnectionClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BuildInterface();
        SelectPage(savedInstanceState?.GetInt("listener-page", 0) ?? 0);
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var appearance = (int)(WindowInsetsControllerAppearance.LightStatusBars | WindowInsetsControllerAppearance.LightNavigationBars);
            Window?.InsetsController?.SetSystemBarsAppearance(appearance, appearance);
        }
        UpdateStatus();
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        outState.PutInt("listener-page", _selectedPage);
        base.OnSaveInstanceState(outState);
    }

    private View CreateHistoryPane()
    {
        var pane = new LinearLayout(this) { Orientation = Orientation.Vertical };
        pane.SetPadding(Dp(18), Dp(8), Dp(18), 0);
        pane.AddView(Label("Historial de BBVA", 23, true));
        _summary = Label("", 14);
        _summary.SetPadding(0, Dp(12), 0, Dp(8));
        pane.AddView(_summary);
        var retry = ActionButton("Reintentar envíos pendientes");
        retry.Click += (_, _) =>
        {
            BbvaHistory.RetryPending(this, includeRejected: true);
            UpdateStatus();
            Toast.MakeText(this, "Se reintentará el envío de los abonos pendientes.", ToastLength.Short)?.Show();
        };
        ListenerUi.Add(pane, retry, 4, 8);
        pane.AddView(Label("Toca un aviso para ver el mensaje original.\nLa recarga se valida en Tony's Go.", 12));
        _adapter = new BbvaHistoryAdapter(this);
        var list = new ListView(this) { Adapter = _adapter, Divider = new global::Android.Graphics.Drawables.ColorDrawable(Color.Transparent), DividerHeight = Dp(8) };
        list.ItemClick += (_, args) => ShowNotification(_adapter[args.Position]);
        var empty = Label("Todavía no hay avisos de BBVA.\n\nConecta este celular y activa la lectura desde Ajustes. Los avisos captados quedan guardados aunque no haya internet.", 16);
        empty.SetPadding(Dp(8), Dp(32), Dp(8), Dp(8));
        pane.AddView(empty);
        pane.AddView(list, new LinearLayout.LayoutParams(-1, 0, 1));
        list.EmptyView = empty;
        return pane;
    }

    private View CreateConfigurationPane()
    {
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(0, Dp(16), 0, 0);
        content.AddView(Label("Conecta este celular", 20, true));
        content.AddView(Label("En la web de Listener, entra como administrador y genera un QR. Escanéalo aquí para conectar automáticamente este teléfono.", 15));
        _linkButton = ActionButton("Escanear QR de vinculación");
        _linkButton.Click += async (_, _) => await LinkAsync();
        ListenerUi.Add(content, _linkButton, 14, 10);
        var permissions = ActionButton("Abrir acceso a notificaciones");
        permissions.Click += (_, _) => StartActivity(new Intent(Settings.ActionNotificationListenerSettings));
        ListenerUi.Add(content, permissions, 0, 10);
        var test = ActionButton("Probar conexión con Listener");
        _checkButton = test;
        test.Click += async (_, _) => await TestConnectionAsync(test);
        ListenerUi.Add(content, test, 0, 10);
        _connectionResult = Label("", 13);
        _connectionResult.SetTextIsSelectable(true);
        content.AddView(_connectionResult);
        var scroll = new ScrollView(this);
        scroll.AddView(content);
        return scroll;
    }

    private View CreateServicePane()
    {
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(0, Dp(16), 0, 0);
        content.AddView(Label("Permisos y funcionamiento", 20, true));
        content.AddView(Label("Listener mantiene un servicio visible para seguir recibiendo BBVA aunque cierres esta pantalla. Los avisos se guardan antes de intentar enviarlos.", 14));
        _setupHint = Label("", 14);
        ListenerUi.Add(content, _setupHint, 12, 6);
        var reactivate = ActionButton("Comprobar y reactivar escucha");
        reactivate.Click += (_, _) => ReactivateReading();
        ListenerUi.Add(content, reactivate, 10, 10);
        var access = ActionButton("Permitir acceso a notificaciones BBVA");
        access.Click += (_, _) => StartActivity(new Intent(Settings.ActionNotificationListenerSettings));
        ListenerUi.Add(content, access, 0, 10);
        var battery = ActionButton("Permitir batería sin restricciones");
        battery.Click += (_, _) =>
        {
            var power = (PowerManager)GetSystemService(PowerService)!;
            if (!power.IsIgnoringBatteryOptimizations(PackageName!))
                StartActivity(new Intent(Settings.ActionRequestIgnoreBatteryOptimizations, global::Android.Net.Uri.Parse("package:" + PackageName)));
            else Toast.MakeText(this, "Listener ya está excluido de la optimización de batería.", ToastLength.Short)?.Show();
        };
        ListenerUi.Add(content, battery, 0, 10);
        var appSettings = ActionButton("Abrir ajustes de inicio automático");
        appSettings.Click += (_, _) => ShowStartupGuide();
        ListenerUi.Add(content, appSettings, 0, 10);
        content.AddView(Label("Si tu teléfono ofrece Inicio automático, actívalo para Listener. Al reiniciar el equipo, desbloquéalo una vez para que Android habilite los datos de las aplicaciones.", 12));
        ListenerUi.Add(content, Label("Avisos de captura", 20, true), 18, 6);
        var alerts = new CheckBox(this) { Text = "Avisarme cuando se capture un abono", Checked = ListenerNotifications.CaptureEnabled(this) };
        alerts.CheckedChange += (_, e) => ListenerNotifications.SetCaptureEnabled(this, e.IsChecked);
        content.AddView(alerts);
        var permission = ActionButton("Permitir avisos de Listener");
        permission.Click += (_, _) => RequestAlertPermission(true); ListenerUi.Add(content, permission, 10, 10);
        var test = ActionButton("Probar aviso local");
        test.Click += (_, _) =>
        {
            if (!ListenerNotifications.Allowed(this)) { RequestAlertPermission(true); return; }
            ListenerNotifications.Test(this);
        };
        ListenerUi.Add(content, test, 0, 10);
        content.AddView(Label("BBVA capturado: ya está guardado en este teléfono.\nRecibido en la web: el servidor confirmó la recepción.\n\nForzar detención o retirar permisos desde Android detiene la aplicación hasta que la vuelvas a abrir.", 12));
        var scroll = new ScrollView(this); scroll.AddView(content); return scroll;
    }

    private void RequestAlertPermission(bool explicitRequest)
    {
        var preferences = GetSharedPreferences("listener_runtime", FileCreationMode.Private)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && CheckSelfPermission("android.permission.POST_NOTIFICATIONS") != global::Android.Content.PM.Permission.Granted)
        {
            var asked = preferences.GetBoolean("asked_notifications", false);
            if (!asked || explicitRequest && ShouldShowRequestPermissionRationale("android.permission.POST_NOTIFICATIONS"))
            {
                preferences.Edit()!.PutBoolean("asked_notifications", true)!.Apply();
                RequestPermissions(["android.permission.POST_NOTIFICATIONS"], 7301);
                return;
            }
            if (!explicitRequest) return;
        }
        if (explicitRequest) StartActivity(new Intent(Settings.ActionAppNotificationSettings).PutExtra(Settings.ExtraAppPackage, PackageName));
    }

    protected override void OnResume()
    {
        base.OnResume();
        _screenVisible = true;
        ListenerRuntimeService.EnsureStarted(this);
        ListenerRecoveryWorker.Schedule(this);
        RequestAlertPermission(false);
        UpdateStatus();
        _refreshCancellation?.Cancel();
        _refreshCancellation = new CancellationTokenSource();
        _nextCheck = DateTimeOffset.MinValue;
        CheckWebIfNeeded();
        _ = RefreshAsync(_refreshCancellation.Token);
    }

    protected override void OnPause()
    {
        _screenVisible = false;
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = null;
        base.OnPause();
    }

    private async Task LinkAsync()
    {
        if (_linking) return;
        _linking = true;
        _linkButton!.Enabled = false;
        try
        {
            _connectionResult!.Text = "Preparando el lector QR. La primera vez puede necesitar una descarga…";
            var raw = await DevicePairingClient.ScanAsync(this);
            if (IsFinishing || IsDestroyed) return;
            if (raw is null) { _connectionResult.Text = "Escaneo cancelado."; return; }
            var allowLocalHttp = false;
#if DEBUG
            allowLocalHttp = true;
#endif
            var qr = DevicePairingProtocol.Parse(raw, allowLocalHttp, DateTimeOffset.UtcNow);
            var confirm = new TaskCompletionSource<bool>();
            var dialog = new AlertDialog.Builder(this)!.SetTitle("Confirmar servidor")!
                .SetMessage($"¿Vincular este teléfono con {new Uri(qr.ServerUrl).Authority}?\n\nLos abonos de BBVA Empresas se enviarán a ese servidor.")!
                .SetPositiveButton("Vincular", (_, _) => confirm.TrySetResult(true))!
                .SetNegativeButton("Cancelar", (_, _) => confirm.TrySetResult(false))!.Create()!;
            dialog.CancelEvent += (_, _) => confirm.TrySetResult(false);
            dialog.Show();
            if (!await confirm.Task) { _connectionResult.Text = "Vinculación cancelada."; return; }
            _connectionResult.Text = "Vinculando el teléfono…";
            await DevicePairingClient.PairAsync(this, qr);
            _webState = WebState.Unknown;
            _nextCheck = DateTimeOffset.MinValue;
            _connectionResult.Text = "Teléfono vinculado. Los abonos pendientes se enviarán automáticamente.";
            ListenerDebugLog.Info(this, "Vinculación QR completada.");
        }
        catch (Exception exception)
        {
            _connectionResult!.Text = exception is FormatException or InvalidOperationException ? exception.Message
                : "No se pudo completar la vinculación. Comprueba la conexión y genera un nuevo QR.";
            ListenerDebugLog.Warning(this, $"Vinculación QR: {exception.GetType().Name}.");
        }
        finally { _linking = false; if (!IsFinishing && !IsDestroyed) { _linkButton.Enabled = true; UpdateStatus(); CheckWebIfNeeded(); } }
    }

    private async Task TestConnectionAsync(Button button)
    {
        button.Enabled = false;
        try { await CheckWebAsync(true); }
        finally { if (!IsFinishing && !IsDestroyed) button.Enabled = true; }
    }

    private void UpdateStatus()
    {
        var settings = ListenerConfig.Load(this);
        var configured = settings.IsValid;
        var permission = NotificationAccess.IsGranted(this);
        if (_status is not null)
            _status.Text = $"Vinculación: {(configured ? "activa" : "pendiente · escanea el QR de la web")}\n" +
                (configured ? $"Servidor: {new Uri(settings.Endpoint).Authority}\n" : "") +
                $"Acceso a notificaciones: {(permission ? "habilitado" : "pendiente")}\n\n{ListenerConfig.LastStatus(this)}\n\nSe capturan únicamente avisos de BBVA Empresas.";
        if (_debugLog is not null) _debugLog.Text = ListenerDebugLog.Read(this);
        var store = BbvaHistory.Store(this);
        var pending = store.PendingCount;
        UpdateDashboard(settings, permission, pending, store);
        if (_serviceState is not null)
        {
            var power = (PowerManager)GetSystemService(PowerService)!;
            _serviceState.Text = $"Lectura de avisos BBVA: {(YapeNotificationListenerService.Connected ? "activa" : !permission ? "falta permiso" : ListenerRuntimeService.NeedsHelp ? "requiere reparar acceso" : "reconectando con Android")}\n" +
                $"Servicio continuo: {(ListenerRuntimeService.Running ? "en ejecución" : "no está activo")}\n" +
                $"Avisos de Listener: {(ListenerNotifications.Allowed(this) ? "permitidos" : "bloqueados en Android")}\n" +
                $"Batería: {(power.IsIgnoringBatteryOptimizations(PackageName!) ? "sin optimización" : "revisa las restricciones")}\n" +
                $"Abonos guardados pendientes: {pending}" + (ListenerRuntimeService.LastError is { } error ? "\n" + error : "");
        }
        var pendingLabel = pending == 1 ? "1 envío pendiente" : $"{pending} envíos pendientes";
        _summary!.Text = $"{pendingLabel} · se muestran los últimos 200 avisos\n" +
            (!permission ? "Activa la lectura de BBVA desde Inicio o Ajustes." :
                !configured ? "Conecta este celular desde Ajustes para enviar los abonos." : "Los avisos anteriores siguen guardados en este celular.");
        if (store.Revision == _historyRevision) return;
        var entries = store.Read();
        _historyRevision = store.Revision;
        _adapter!.Replace(entries);
        UpdateLatestNotice(entries.FirstOrDefault());
    }

    private void ShowNotification(BbvaHistoryEntry entry)
    {
        ShowFriendlyNotification(entry);
    }

    internal static string Money(decimal? amount) => amount is decimal value
        ? $"S/ {value.ToString("N2", CultureInfo.GetCultureInfo("es-PE"))}" : "Sin importe";
    internal static string StatusLabel(BbvaDeliveryStatus status) => status switch
    {
        BbvaDeliveryStatus.StoredByListener => "Recibido en la web de Listener",
        BbvaDeliveryStatus.Pending => "Guardado · por enviar",
        BbvaDeliveryStatus.Sending => "Enviando a la web…",
        BbvaDeliveryStatus.Retry => "Guardado · volveremos a intentar",
        BbvaDeliveryStatus.Rejected => "Guardado · necesita revisión",
        BbvaDeliveryStatus.ConfigurationRequired => "Guardado · falta conectar con QR",
        _ => "Aviso informativo · solo historial"
    };
    private TextView Label(string text, float size, bool bold = false)
    {
        return ListenerUi.Text(this, text, size, bold);
    }
    private Button ActionButton(string text)
    {
        return ListenerUi.Button(this, text);
    }
    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density);
    private async Task RefreshAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
                RunOnUiThread(() => { if (_screenVisible && !IsFinishing && !IsDestroyed) { UpdateStatus(); CheckWebIfNeeded(); } });
            }
        }
        catch (System.OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}

[SupportedOSPlatform("android30.0")]
internal sealed class SystemBarsInsets(int horizontal, int top, int bottom) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
{
    public WindowInsets OnApplyWindowInsets(View? view, WindowInsets? insets)
    {
        if (view is not null && insets is not null)
        {
            var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
            view.SetPadding(horizontal + bars.Left, top + bars.Top, horizontal + bars.Right, bottom + bars.Bottom);
        }
        return insets!;
    }
}

internal sealed class BbvaHistoryAdapter(Context context) : BaseAdapter<BbvaHistoryEntry>
{
    private IReadOnlyList<BbvaHistoryEntry> _entries = [];
    public override int Count => _entries.Count;
    public override BbvaHistoryEntry this[int position] => _entries[position];
    public override long GetItemId(int position) => position;
    public void Replace(IReadOnlyList<BbvaHistoryEntry> entries) { _entries = entries; NotifyDataSetChanged(); }
    public override View GetView(int position, View? convertView, ViewGroup? parent)
    {
        var row = convertView as LinearLayout;
        if (row is null)
        {
            row = new LinearLayout(context) { Orientation = Orientation.Vertical };
            var padding = (int)(14 * context.Resources!.DisplayMetrics!.Density);
            row.SetPadding(padding, padding, padding, padding);
            row.Background = ListenerUi.Shape(context, ListenerUi.Surface, 18, true);
            foreach (var size in new float[] { 16, 21, 13, 12 })
            {
                var label = new TextView(context) { TextSize = size };
                label.SetTextColor(ListenerUi.Ink);
                label.SetLineSpacing(ListenerUi.Dp(context, 2), 1);
                row.AddView(label);
            }
            ((TextView)row.GetChildAt(0)!).SetTypeface(null, TypefaceStyle.Bold);
        }
        var entry = _entries[position];
        ((TextView)row.GetChildAt(0)!).Text = entry.Payment.NombreCompleto ?? entry.Notification.Title ?? "Aviso BBVA Empresas";
        ((TextView)row.GetChildAt(1)!).Text = MainActivity.Money(entry.Payment.Monto);
        var status = (TextView)row.GetChildAt(2)!;
        status.Text = MainActivity.StatusLabel(entry.Status);
        status.SetTextColor(entry.Status == BbvaDeliveryStatus.StoredByListener ? ListenerUi.Green
            : entry.Status == BbvaDeliveryStatus.Rejected ? ListenerUi.Red
            : entry.Status == BbvaDeliveryStatus.HistoryOnly ? ListenerUi.Muted : ListenerUi.Amber);
        ((TextView)row.GetChildAt(3)!).Text = entry.Notification.PostedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
        ((TextView)row.GetChildAt(3)!).SetTextColor(ListenerUi.Muted);
        return row;
    }
}

internal static class NotificationAccess
{
    public static bool IsGranted(Context context)
    {
        var enabled = Settings.Secure.GetString(context.ContentResolver, "enabled_notification_listeners") ?? "";
        return enabled.Split(':').Any(value => ComponentName.UnflattenFromString(value)?.PackageName == context.PackageName);
    }
}
