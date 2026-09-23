using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;

namespace TonysGo.YapeListener.Android;

[Activity(
    Label = "Tony's Go Yape Listener",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public sealed class MainActivity : Activity
{
    private EditText? _endpoint;
    private EditText? _device;
    private EditText? _apiKey;
    private TextView? _status;
    private TextView? _debugLog;
    private CancellationTokenSource? _refreshCancellation;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var settings = ListenerConfig.Load(this);
        var padding = (int)(20 * Resources!.DisplayMetrics!.Density);

        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        root.SetPadding(padding, padding, padding, padding);

        var title = new TextView(this)
        {
            Text = "Tony's Go - Listener de Yape",
            TextSize = 22
        };

        _endpoint = CreateInput("https://listener.tudominio.com (local: http://127.0.0.1:5057)", settings.Endpoint);
        _device = CreateInput("Dispositivo, por ejemplo yape-caja-01", settings.DeviceId);
        _apiKey = CreateInput("Clave del colector (minimo 32 caracteres)", settings.ApiKey);
        _apiKey.InputType = InputTypes.ClassText | InputTypes.TextVariationPassword;

        var save = new Button(this) { Text = "Guardar configuracion" };
        save.Click += (_, _) => SaveConfiguration();

        var permissions = new Button(this) { Text = "Abrir acceso a notificaciones" };
        permissions.Click += (_, _) =>
            StartActivity(new Intent(Settings.ActionNotificationListenerSettings));

        var refresh = new Button(this) { Text = "Actualizar estado" };
        refresh.Click += (_, _) => UpdateStatus();

        var clearLog = new Button(this) { Text = "Limpiar registro de depuracion" };
        clearLog.Click += (_, _) =>
        {
            ListenerDebugLog.Clear(this);
            UpdateStatus();
        };

        _status = new TextView(this);
        _debugLog = new TextView(this)
        {
            TextSize = 11
        };
        _debugLog.SetTextIsSelectable(true);
        var debugScroll = new ScrollView(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                0,
                1f)
        };
        debugScroll.AddView(_debugLog);

        root.AddView(title);
        root.AddView(_endpoint);
        root.AddView(_device);
        root.AddView(_apiKey);
        root.AddView(save);
        root.AddView(permissions);
        root.AddView(refresh);
        root.AddView(_status);
        root.AddView(clearLog);
        root.AddView(debugScroll);

        SetContentView(root);
        UpdateStatus();
    }

    protected override void OnResume()
    {
        base.OnResume();
        UpdateStatus();
        _refreshCancellation?.Cancel();
        _refreshCancellation = new CancellationTokenSource();
        _ = RefreshDebugLogAsync(_refreshCancellation.Token);
    }

    protected override void OnPause()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = null;
        base.OnPause();
    }

    private EditText CreateInput(string hint, string value)
    {
        var input = new EditText(this)
        {
            Hint = hint,
            Text = value,
            ImeOptions = ImeAction.Next
        };
        input.SetSingleLine(true);
        return input;
    }

    private void SaveConfiguration()
    {
        var settings = new ListenerSettings(
            _endpoint?.Text?.Trim().TrimEnd('/') ?? string.Empty,
            _device?.Text?.Trim() ?? string.Empty,
            _apiKey?.Text?.Trim() ?? string.Empty);

        if (!settings.IsValid)
        {
            SetStatus("Configura una URL HTTPS valida (o 127.0.0.1 en Debug), un identificador y una clave de al menos 32 caracteres.");
            return;
        }

        ListenerConfig.Save(this, settings);
        SetStatus("Configuracion guardada. Habilita ahora el acceso a notificaciones.");
    }

    private void UpdateStatus()
    {
        if (_status is null)
        {
            return;
        }

        var configured = ListenerConfig.Load(this).IsValid;
        var permissionGranted = NotificationAccess.IsGranted(this);
        _status.Text =
            $"Configuracion: {(configured ? "lista" : "pendiente")}\n" +
            $"Acceso a notificaciones: {(permissionGranted ? "habilitado" : "pendiente")}\n\n" +
            "Ultimo evento:\n" + ListenerConfig.LastStatus(this) + "\n\n" +
            "Solo se enviaran notificaciones de la aplicacion oficial de Yape.";
        if (_debugLog is not null)
        {
            _debugLog.Text = "\nRegistro de depuracion (mas reciente primero):\n" + ListenerDebugLog.Read(this);
        }
    }

    private void SetStatus(string message)
    {
        if (_status is not null)
        {
            _status.Text = message;
        }
    }

    private async Task RefreshDebugLogAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                RunOnUiThread(UpdateStatus);
            }
        }
        catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Activity paused: the next OnResume starts a fresh UI refresh loop.
        }
    }
}

internal static class NotificationAccess
{
    public static bool IsGranted(Context context)
    {
        var enabled = Settings.Secure.GetString(
            context.ContentResolver,
            "enabled_notification_listeners") ?? string.Empty;
        return enabled.Contains(context.PackageName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
