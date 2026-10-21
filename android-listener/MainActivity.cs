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

[Activity(Label = "Tony's Go BBVA Listener", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public sealed class MainActivity : Activity
{
    private Button? _linkButton;
    private TextView? _status, _connectionResult, _debugLog, _summary;
    private View? _historyPane, _configurationPane;
    private BbvaHistoryAdapter? _adapter;
    private long _historyRevision = -1;
    private CancellationTokenSource? _refreshCancellation;
    private static readonly HttpClient ConnectionClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(Dp(16), Dp(20), Dp(16), Dp(12));
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            root.SetOnApplyWindowInsetsListener(new SystemBarsInsets(Dp(16), Dp(20), Dp(12)));
        else
            root.SetFitsSystemWindows(true);
        root.SetBackgroundColor(Color.Rgb(244, 247, 250));
        root.AddView(Label("Tony's Go · BBVA", 24, true));
        root.AddView(Label("Notificaciones y abonos recibidos", 14));
        var tabs = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var historyTab = ActionButton("Historial BBVA");
        var configurationTab = ActionButton("Conexión");
        tabs.AddView(historyTab, new LinearLayout.LayoutParams(0, Dp(52), 1));
        tabs.AddView(configurationTab, new LinearLayout.LayoutParams(0, Dp(52), 1));
        root.AddView(tabs);
        _historyPane = CreateHistoryPane();
        _configurationPane = CreateConfigurationPane();
        root.AddView(_historyPane, new LinearLayout.LayoutParams(-1, 0, 1));
        root.AddView(_configurationPane, new LinearLayout.LayoutParams(-1, 0, 1));
        void SelectTab(bool history)
        {
            _historyPane.Visibility = history ? ViewStates.Visible : ViewStates.Gone;
            _configurationPane.Visibility = history ? ViewStates.Gone : ViewStates.Visible;
            historyTab.SetTextColor(history ? Color.Rgb(0, 92, 144) : Color.DarkGray);
            configurationTab.SetTextColor(history ? Color.DarkGray : Color.Rgb(0, 92, 144));
        }
        historyTab.Click += (_, _) => SelectTab(true);
        configurationTab.Click += (_, _) => SelectTab(false);
        SelectTab(true);
        SetContentView(root);
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var appearance = (int)(WindowInsetsControllerAppearance.LightStatusBars | WindowInsetsControllerAppearance.LightNavigationBars);
            Window?.InsetsController?.SetSystemBarsAppearance(appearance, appearance);
        }
        UpdateStatus();
    }

    private View CreateHistoryPane()
    {
        var pane = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _summary = Label("", 14);
        _summary.SetPadding(0, Dp(12), 0, Dp(8));
        pane.AddView(_summary);
        var retry = ActionButton("Reintentar envíos pendientes");
        retry.Click += (_, _) =>
        {
            BbvaHistory.RetryPending(this);
            UpdateStatus();
            Toast.MakeText(this, "Se reintentará el envío de los abonos pendientes.", ToastLength.Short)?.Show();
        };
        pane.AddView(retry);
        pane.AddView(Label("Toca un aviso para ver el mensaje original.\nLa recarga se valida en Tony's Go.", 12));
        _adapter = new BbvaHistoryAdapter(this);
        var list = new ListView(this) { Adapter = _adapter, DividerHeight = Dp(8) };
        list.ItemClick += (_, args) => ShowNotification(_adapter[args.Position]);
        var empty = Label("Todavía no hay notificaciones de BBVA Empresas.\n\nHabilita el acceso en Conexión. Los avisos se conservarán aquí aunque no haya conexión.", 16);
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
        content.AddView(Label("Vincula este teléfono", 22, true));
        content.AddView(Label("En la web de Listener, entra como administrador y genera un QR. Escanéalo aquí para conectar automáticamente este teléfono.", 15));
        _linkButton = ActionButton("Escanear QR de vinculación");
        _linkButton.Click += async (_, _) => await LinkAsync();
        content.AddView(_linkButton);
        var permissions = ActionButton("Abrir acceso a notificaciones");
        permissions.Click += (_, _) => StartActivity(new Intent(Settings.ActionNotificationListenerSettings));
        content.AddView(permissions);
        var test = ActionButton("Probar conexión con Listener");
        test.Click += async (_, _) => await TestConnectionAsync(test);
        content.AddView(test);
        _connectionResult = Label("", 13);
        _connectionResult.SetTextIsSelectable(true);
        content.AddView(_connectionResult);
        _status = Label("", 14);
        content.AddView(_status);
        content.AddView(Label("Diagnóstico de conexión", 18, true));
        var clear = ActionButton("Limpiar diagnóstico");
        clear.Click += (_, _) => { ListenerDebugLog.Clear(this); UpdateStatus(); };
        content.AddView(clear);
        content.AddView(Label("El historial BBVA se conserva al limpiar el diagnóstico.", 12));
        _debugLog = Label("", 11);
        _debugLog.SetTextIsSelectable(true);
        content.AddView(_debugLog);
        var scroll = new ScrollView(this);
        scroll.AddView(content);
        return scroll;
    }

    protected override void OnResume()
    {
        base.OnResume();
        UpdateStatus();
        _refreshCancellation?.Cancel();
        _refreshCancellation = new CancellationTokenSource();
        _ = RefreshAsync(_refreshCancellation.Token);
    }

    protected override void OnPause()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = null;
        base.OnPause();
    }

    private async Task LinkAsync()
    {
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
            _connectionResult.Text = "Teléfono vinculado. Los abonos pendientes se enviarán automáticamente.";
            ListenerDebugLog.Info(this, "Vinculación QR completada.");
        }
        catch (Exception exception)
        {
            _connectionResult!.Text = exception is FormatException or InvalidOperationException ? exception.Message
                : "No se pudo completar la vinculación. Comprueba la conexión y genera un nuevo QR.";
            ListenerDebugLog.Warning(this, $"Vinculación QR: {exception.GetType().Name}.");
        }
        finally { if (!IsFinishing && !IsDestroyed) { _linkButton.Enabled = true; UpdateStatus(); } }
    }

    private async Task TestConnectionAsync(Button button)
    {
        var settings = ListenerConfig.Load(this);
        if (!settings.IsValid) { _connectionResult!.Text = "Escanea primero el QR de vinculación generado en la web."; return; }
        button.Enabled = false;
        _connectionResult!.Text = "Consultando Listener...";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{settings.Endpoint}/api/v1/bbva/connection");
            request.Headers.Add("X-Listener-Api-Key", settings.ApiKey);
            using var response = await ConnectionClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var cleared = ListenerConfig.ClearPairing(this, settings.ApiKey);
                _connectionResult.Text = cleared ? "El acceso ya no está activo. Genera un nuevo QR en la web y vuelve a vincular este teléfono."
                    : "La vinculación se actualizó durante la prueba. Vuelve a comprobar la conexión.";
            }
            else _connectionResult.Text = response.IsSuccessStatusCode ? "Conexión correcta. Listener reconoce este teléfono."
                : $"Listener respondió HTTP {(int)response.StatusCode}. Intenta nuevamente en unos momentos.";
            ListenerDebugLog.Info(this, $"Prueba de conexión BBVA: HTTP {(int)response.StatusCode}.");
            UpdateStatus();
        }
        catch (Exception exception)
        {
            _connectionResult.Text = $"Sin respuesta: {exception.GetType().Name}: {exception.Message}";
            ListenerDebugLog.Warning(this, $"Prueba de conexión: {exception.GetType().Name}.");
        }
        finally { button.Enabled = true; }
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
        var entries = store.Read();
        var pending = entries.Count(x => x.Payment.EsAbono && x.Status != BbvaDeliveryStatus.StoredByListener);
        var pendingLabel = pending == 1 ? "1 envío pendiente" : $"{pending} envíos pendientes";
        _summary!.Text = $"{entries.Count} avisos guardados · {pendingLabel}\n" +
            (!permission ? "Habilita el acceso a notificaciones en Conexión." :
                !configured ? "Vincula este teléfono desde Conexión para enviar los abonos." : "Acceso habilitado · últimas 200 notificaciones");
        if (store.Revision == _historyRevision) return;
        _historyRevision = store.Revision;
        _adapter!.Replace(entries);
    }

    private void ShowNotification(BbvaHistoryEntry entry)
    {
        var notification = entry.Notification;
        var body = $"Fecha del aviso: {notification.PostedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss}\n" +
            $"Nombre: {entry.Payment.NombreCompleto ?? "No extraído"}\nMonto: {Money(entry.Payment.Monto)}\n" +
            $"Estado: {StatusLabel(entry.Status)}\n{entry.Detail}\nIntentos de envío: {entry.Attempts}\n\n" +
            $"Título original:\n{notification.Title ?? "(vacío)"}\n\nMensaje original:\n{notification.Text ?? "(vacío)"}\n\n" +
            $"Mensaje ampliado:\n{notification.BigText ?? "(vacío)"}\n\nMotivo: {entry.Payment.CodigoMotivo}\nEvento: {notification.NotificationKey}";
        var text = Label(body, 15);
        text.SetPadding(Dp(18), Dp(12), Dp(18), Dp(12));
        text.SetTextIsSelectable(true);
        var scroll = new ScrollView(this);
        scroll.AddView(text);
        new AlertDialog.Builder(this)!.SetTitle(notification.Title ?? "Aviso BBVA Empresas")!
            .SetView(scroll)!.SetPositiveButton("Cerrar", (_, _) => { })!.Show();
    }

    internal static string Money(decimal? amount) => amount is decimal value
        ? $"S/ {value.ToString("N2", CultureInfo.GetCultureInfo("es-PE"))}" : "Sin importe";
    internal static string StatusLabel(BbvaDeliveryStatus status) => status switch
    {
        BbvaDeliveryStatus.StoredByListener => "Recibido en Listener",
        BbvaDeliveryStatus.Pending => "Pendiente de envío",
        BbvaDeliveryStatus.Sending => "Enviando a Listener…",
        BbvaDeliveryStatus.Retry => "Esperando reintento",
        BbvaDeliveryStatus.Rejected => "Envío rechazado",
        BbvaDeliveryStatus.ConfigurationRequired => "Teléfono sin vincular",
        _ => "Solo historial · sin abono reconocido"
    };
    private TextView Label(string text, float size, bool bold = false)
    {
        var label = new TextView(this) { Text = text, TextSize = size };
        label.SetTextColor(Color.Rgb(33, 48, 65));
        if (bold) label.SetTypeface(null, TypefaceStyle.Bold);
        return label;
    }
    private Button ActionButton(string text)
    {
        var button = new Button(this) { Text = text };
        button.SetAllCaps(false);
        return button;
    }
    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density);
    private async Task RefreshAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
                RunOnUiThread(UpdateStatus);
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
            row.SetBackgroundColor(Color.White);
            foreach (var size in new float[] { 16, 21, 13, 12 })
            {
                var label = new TextView(context) { TextSize = size };
                label.SetTextColor(Color.Rgb(33, 48, 65));
                row.AddView(label);
            }
            ((TextView)row.GetChildAt(0)!).SetTypeface(null, TypefaceStyle.Bold);
        }
        var entry = _entries[position];
        ((TextView)row.GetChildAt(0)!).Text = entry.Payment.NombreCompleto ?? entry.Notification.Title ?? "Aviso BBVA Empresas";
        ((TextView)row.GetChildAt(1)!).Text = MainActivity.Money(entry.Payment.Monto);
        var status = (TextView)row.GetChildAt(2)!;
        status.Text = MainActivity.StatusLabel(entry.Status);
        status.SetTextColor(entry.Status == BbvaDeliveryStatus.StoredByListener ? Color.Rgb(0, 112, 92) : Color.Rgb(83, 91, 103));
        ((TextView)row.GetChildAt(3)!).Text = entry.Notification.PostedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
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
