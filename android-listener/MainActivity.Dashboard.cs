using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Net;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using System.Text.Json;
using TonysGo.Listener.Bbva;
using OperationCanceledException = System.OperationCanceledException;

namespace TonysGo.YapeListener.Android;

public sealed partial class MainActivity
{
    private enum WebState { Unknown, Checking, Available, Unavailable, NeedsPairing }
    private enum DashboardAction { Check, Link, Permission, Reactivate, Repair, Internet }
    private View _homePane = null!, _settingsPane = null!;
    private readonly List<(LinearLayout Item, ListenerIcon Icon, TextView Label)> _navigation = [];
    private int _selectedPage;
    private TextView _stateTitle = null!, _stateMessage = null!, _stateChecked = null!, _queueTitle = null!, _queueMessage = null!;
    private TextView _lastName = null!, _lastAmount = null!, _lastDate = null!, _lastState = null!, _setupHint = null!, _technicalConnection = null!;
    private ListenerIcon _stateIcon = null!, _queueIcon = null!;
    private ListenerCheckRow _readingCheck = null!, _internetCheck = null!, _webCheck = null!;
    private Button _homeButton = null!, _checkButton = null!;
    private LinearLayout _lastNotice = null!;
    private BbvaHistoryEntry? _latest;
    private Action? _openConfiguration;
    private string? _stateSignature, _checkedCredential, _checkedEndpoint;
    private bool _screenVisible, _checking, _linking, _previousInternet;
    private DateTimeOffset _nextCheck, _lastCheck;
    private WebState _webState;
    private DashboardAction _dashboardAction;

    private void BuildInterface()
    {
        var root = ListenerUi.Column(this);
        root.SetBackgroundColor(ListenerUi.Background);
        if (OperatingSystem.IsAndroidVersionAtLeast(30)) root.SetOnApplyWindowInsetsListener(new SystemBarsInsets(0, 0, 0));
        else root.SetFitsSystemWindows(true);
        var header = ListenerUi.Row(this);
        header.SetPadding(Dp(20), Dp(18), Dp(20), Dp(16));
        header.SetBackgroundColor(ListenerUi.Surface);
        var logo = new ImageView(this) { ImportantForAccessibility = ImportantForAccessibility.No };
        logo.SetImageResource(Resource.Mipmap.ic_launcher);
        logo.SetScaleType(ImageView.ScaleType.FitCenter);
        header.AddView(logo, new LinearLayout.LayoutParams(Dp(42), Dp(42)) { RightMargin = Dp(12) });
        var heading = ListenerUi.Column(this);
        heading.AddView(Label("Tony’s Go Listener", 18, true));
        heading.AddView(ListenerUi.Text(this, "BBVA Empresas", 13, color: ListenerUi.Muted));
        header.AddView(heading, new LinearLayout.LayoutParams(0, -2, 1));
        root.AddView(header);
        _homePane = CreateHomePane();
        _historyPane = CreateHistoryPane();
        _settingsPane = CreateSettingsPane();
        foreach (var page in new[] { _homePane, _historyPane, _settingsPane })
            root.AddView(page, new LinearLayout.LayoutParams(-1, 0, 1));
        var nav = ListenerUi.Row(this);
        nav.SetBackgroundColor(ListenerUi.Surface);
        nav.SetPadding(Dp(10), Dp(8), Dp(10), Dp(10));
        foreach (var (title, glyph) in new[] { ("Inicio", ListenerGlyph.Home), ("Historial", ListenerGlyph.History), ("Ajustes", ListenerGlyph.Settings) })
        {
            var index = _navigation.Count;
            var item = ListenerUi.Column(this);
            item.SetGravity(GravityFlags.Center);
            item.SetPadding(Dp(8), Dp(8), Dp(8), Dp(8));
            item.SetMinimumHeight(Dp(62));
            item.Clickable = true; item.Focusable = true; item.ContentDescription = title;
            var icon = new ListenerIcon(this, glyph, ListenerUi.Muted);
            item.AddView(icon, new LinearLayout.LayoutParams(Dp(23), Dp(23)) { BottomMargin = Dp(5) });
            var caption = ListenerUi.Text(this, title, 12, true, ListenerUi.Muted);
            caption.Gravity = GravityFlags.Center;
            caption.ImportantForAccessibility = ImportantForAccessibility.No;
            item.AddView(caption);
            item.Click += (_, _) => SelectPage(index);
            _navigation.Add((item, icon, caption));
            nav.AddView(item, new LinearLayout.LayoutParams(0, -2, 1) { LeftMargin = Dp(3), RightMargin = Dp(3) });
        }
        root.AddView(nav);
        SetContentView(root);
    }

    private View CreateHomePane()
    {
        var content = ListenerUi.Column(this);
        content.SetPadding(Dp(18), Dp(20), Dp(18), Dp(20));
        var title = ListenerUi.Row(this);
        _stateIcon = new ListenerIcon(this, ListenerGlyph.Refresh, ListenerUi.Brand)
        { Background = ListenerUi.Shape(this, ListenerUi.BrandSoft, 28) };
        title.AddView(_stateIcon, new LinearLayout.LayoutParams(Dp(48), Dp(48)) { RightMargin = Dp(13) });
        var heading = ListenerUi.Column(this);
        _stateTitle = Label("Comprobando este celular", 22, true);
        _stateTitle.AccessibilityLiveRegion = AccessibilityLiveRegion.Polite;
        _stateChecked = ListenerUi.Text(this, "", 12, color: ListenerUi.Muted);
        heading.AddView(_stateTitle); ListenerUi.Add(heading, _stateChecked, 5);
        title.AddView(heading, new LinearLayout.LayoutParams(0, -2, 1));
        content.AddView(title);
        _stateMessage = ListenerUi.Text(this, "Estamos revisando la lectura de BBVA y la conexión con la web.", 15, color: ListenerUi.Muted);
        ListenerUi.Add(content, _stateMessage, 17, 16);
        var checks = ListenerUi.Card(this);
        checks.SetPadding(Dp(15), Dp(2), Dp(15), Dp(2));
        _readingCheck = new(this, ListenerGlyph.Reader, "Lectura de BBVA");
        _internetCheck = new(this, ListenerGlyph.Internet, "Internet");
        _webCheck = new(this, ListenerGlyph.Cloud, "Conexión con la web");
        checks.AddView(_readingCheck); AddRule(checks);
        checks.AddView(_internetCheck); AddRule(checks);
        checks.AddView(_webCheck);
        content.AddView(checks);
        _homeButton = ListenerUi.Button(this, "Comprobar ahora", true);
        _homeButton.Click += async (_, _) => await RunDashboardActionAsync();
        ListenerUi.Add(content, _homeButton, 16, 4);
        var queue = ListenerUi.Row(this);
        queue.SetPadding(Dp(2), Dp(17), Dp(2), Dp(17));
        _queueIcon = new(this, ListenerGlyph.Cloud, ListenerUi.Muted);
        queue.AddView(_queueIcon, new LinearLayout.LayoutParams(Dp(24), Dp(24)) { RightMargin = Dp(12) });
        var queueText = ListenerUi.Column(this);
        _queueTitle = Label("", 14, true);
        _queueMessage = ListenerUi.Text(this, "", 13, color: ListenerUi.Muted);
        queueText.AddView(_queueTitle); ListenerUi.Add(queueText, _queueMessage, 3);
        queue.AddView(queueText, new LinearLayout.LayoutParams(0, -2, 1));
        content.AddView(queue);
        var lastHeading = ListenerUi.Row(this);
        lastHeading.AddView(Label("Último aviso", 17, true), new LinearLayout.LayoutParams(0, -2, 1));
        var history = ListenerUi.Button(this, "Ver historial");
        history.Click += (_, _) => SelectPage(1);
        lastHeading.AddView(history, new LinearLayout.LayoutParams(-2, -2));
        ListenerUi.Add(content, lastHeading, 6, 10);
        _lastNotice = ListenerUi.Card(this);
        _lastName = Label("Todavía no hay avisos de BBVA", 15, true);
        _lastAmount = Label("", 23, true);
        _lastState = Label("Los avisos que capte este celular aparecerán aquí.", 13);
        _lastDate = ListenerUi.Text(this, "", 12, color: ListenerUi.Muted);
        _lastNotice.AddView(_lastName); ListenerUi.Add(_lastNotice, _lastAmount, 7);
        ListenerUi.Add(_lastNotice, _lastState, 6); ListenerUi.Add(_lastNotice, _lastDate, 6);
        _lastNotice.Click += (_, _) => { if (_latest is not null) ShowNotification(_latest); };
        _lastNotice.Focusable = true;
        content.AddView(_lastNotice);
        var explanation = ListenerUi.Text(this, "Recibido en la web confirma la entrega del aviso a Listener. La recarga se procesa en Tony’s Go.", 12, color: ListenerUi.Muted);
        ListenerUi.Add(content, explanation, 13);
        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.AddView(content); return scroll;
    }

    private View CreateSettingsPane()
    {
        var content = ListenerUi.Column(this);
        content.SetPadding(Dp(18), Dp(16), Dp(18), Dp(20));
        content.AddView(Label("Ajustes", 24, true));
        ListenerUi.Add(content, ListenerUi.Text(this, "Conecta este celular y revisa los permisos que necesita Listener.", 14, color: ListenerUi.Muted), 6, 17);
        _configurationPane = Unwrap(CreateConfigurationPane());
        _servicePane = Unwrap(CreateServicePane());
        _openConfiguration = AddExpandable(content, "Vinculación con QR", _configurationPane, false);
        AddExpandable(content, "Permisos, batería y avisos", _servicePane, false);
        var technical = ListenerUi.Column(this);
        _status = Label("", 14); _status.SetTextIsSelectable(true);
        _serviceState = Label("", 14); _serviceState.SetTextIsSelectable(true);
        _technicalConnection = Label("Todavía no se ha comprobado la web.", 13); _technicalConnection.SetTextIsSelectable(true);
        technical.AddView(_status); ListenerUi.Add(technical, _serviceState, 16);
        ListenerUi.Add(technical, _technicalConnection, 16);
        ListenerUi.Add(technical, Label("Diagnóstico de conexión", 18, true), 20, 10);
        var clear = ActionButton("Limpiar diagnóstico");
        clear.Click += (_, _) => { ListenerDebugLog.Clear(this); UpdateStatus(); };
        technical.AddView(clear);
        ListenerUi.Add(technical, ListenerUi.Text(this, "Limpiar el diagnóstico no borra el historial de BBVA.", 12, color: ListenerUi.Muted), 8, 12);
        _debugLog = Label("", 12); _debugLog.SetTextIsSelectable(true);
        technical.AddView(_debugLog);
        AddExpandable(content, "Ayuda y detalles técnicos", technical, false);
        var scroll = new ScrollView(this) { FillViewport = true }; scroll.AddView(content); return scroll;
    }

    private static View Unwrap(View view)
    {
        var scroll = (ScrollView)view;
        var content = scroll.GetChildAt(0)!; scroll.RemoveView(content); return content;
    }

    private Action AddExpandable(LinearLayout parent, string title, View body, bool expanded)
    {
        var card = ListenerUi.Card(this);
        card.SetPadding(Dp(14), Dp(5), Dp(14), Dp(5));
        var button = ListenerUi.Button(this, "");
        button.Gravity = GravityFlags.CenterVertical | GravityFlags.Left;
        button.SetPadding(0, Dp(10), 0, Dp(10));
        button.Background = new RippleDrawable(ColorStateList.ValueOf(ListenerUi.BrandSoft), ListenerUi.Shape(this, ListenerUi.Surface, 12), null);
        card.AddView(button);
        card.AddView(body, new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(12) });
        void SetExpanded(bool value)
        {
            expanded = value; body.Visibility = value ? ViewStates.Visible : ViewStates.Gone;
            button.Text = title + (value ? "  −" : "  +");
            button.ContentDescription = title + (value ? ", contraer opciones" : ", mostrar opciones");
        }
        button.Click += (_, _) => SetExpanded(!expanded);
        SetExpanded(expanded); ListenerUi.Add(parent, card, 0, 12);
        return () => SetExpanded(true);
    }

    private void AddRule(LinearLayout parent)
    {
        var rule = new View(this); rule.SetBackgroundColor(ListenerUi.Border);
        parent.AddView(rule, new LinearLayout.LayoutParams(-1, Dp(1)));
    }

    private void SelectPage(int index)
    {
        _selectedPage = Math.Clamp(index, 0, 2);
        var pages = new[] { _homePane, _historyPane!, _settingsPane };
        var glyphs = new[] { ListenerGlyph.Home, ListenerGlyph.History, ListenerGlyph.Settings };
        for (var i = 0; i < pages.Length; i++)
        {
            var selected = i == _selectedPage;
            pages[i].Visibility = selected ? ViewStates.Visible : ViewStates.Gone;
            var nav = _navigation[i];
            nav.Item.Selected = selected;
            nav.Item.Background = ListenerUi.Shape(this, selected ? ListenerUi.BrandSoft : ListenerUi.Surface, 14);
            nav.Label.SetTextColor(selected ? ListenerUi.Brand : ListenerUi.Muted);
            nav.Icon.Show(glyphs[i], selected ? ListenerUi.Brand : ListenerUi.Muted);
        }
    }

    private bool HasInternet()
    {
        var manager = (ConnectivityManager)GetSystemService(ConnectivityService)!;
        var network = manager.ActiveNetwork;
        using var capabilities = network is null ? null : manager.GetNetworkCapabilities(network);
        return capabilities?.HasCapability(NetCapability.Validated) == true;
    }

    private void UpdateDashboard(ListenerSettings settings, bool permission, int pending, BbvaLocalDatabase store)
    {
        var reading = permission && YapeNotificationListenerService.Connected && ListenerRuntimeService.Running;
        var internet = HasInternet();
        if (_previousInternet != internet)
        {
            _previousInternet = internet; _nextCheck = DateTimeOffset.MinValue;
            if (!internet) _webState = WebState.Unknown;
        }
        if (settings.Endpoint != _checkedEndpoint || settings.ApiKey != _checkedCredential)
        {
            _webState = WebState.Unknown;
            if (!_checking) _nextCheck = DateTimeOffset.MinValue;
        }
        var good = internet && settings.IsValid && _webState == WebState.Available && DateTimeOffset.UtcNow - _lastCheck < TimeSpan.FromMinutes(1);
        var needsHelp = !reading && ListenerRuntimeService.NeedsHelp;
        _readingCheck.Update(!permission ? "Falta activar el permiso" : reading ? "Activa · recibimos los avisos"
            : needsHelp ? "Lectura detenida · requiere atención" : "Reconectando con Android…",
            !permission || needsHelp ? ListenerUi.Red : reading ? ListenerUi.Green : ListenerUi.Amber, reading, permission && !reading && !needsHelp);
        _internetCheck.Update(internet ? "Disponible" : "Sin conexión", internet ? ListenerUi.Green : ListenerUi.Amber, internet);
        var web = !settings.IsValid ? "Falta conectar este celular con QR" : !internet ? "Esperando internet"
            : _webState == WebState.NeedsPairing ? "Vuelve a conectar este celular con QR"
            : _checking ? "Comprobando la conexión…" : good ? "Comprobada · la web reconoce este celular"
            : _webState == WebState.Unavailable ? "Sin confirmación de la web" : "Pendiente de comprobación";
        _webCheck.Update(web, good ? ListenerUi.Green : _webState == WebState.NeedsPairing ? ListenerUi.Red : ListenerUi.Amber, good, _checking);
        if (!settings.IsValid || _webState == WebState.NeedsPairing)
            SetDashboardState("Conecta este celular", "Escanea el QR de la web de Listener para enviar los avisos de pago.", ListenerUi.Brand, ListenerUi.BrandSoft, ListenerGlyph.Qr, "Conectar con QR", DashboardAction.Link);
        else if (!permission)
            SetDashboardState("Necesitamos tu ayuda", "Activa el permiso para recibir los avisos de BBVA en este celular.", ListenerUi.Red, ListenerUi.RedSoft, ListenerGlyph.Alert, "Activar lectura de BBVA", DashboardAction.Permission);
        else if (needsHelp)
            SetDashboardState("La lectura necesita atención", "Android todavía no ha activado la lectura. Revisa el acceso a notificaciones y el inicio automático. Los avisos ya guardados siguen disponibles.", ListenerUi.Red, ListenerUi.RedSoft, ListenerGlyph.Alert, "Reparar lectura de BBVA", DashboardAction.Repair);
        else if (!reading)
            SetDashboardState("Estamos reconectando", "La lectura de nuevos avisos todavía no está confirmada. Los avisos anteriores siguen guardados.", ListenerUi.Amber, ListenerUi.AmberSoft, ListenerGlyph.Refresh, "Comprobar y reactivar", DashboardAction.Reactivate);
        else if (!internet)
            SetDashboardState("Sin internet", "Seguimos guardando los avisos de BBVA en este celular.", ListenerUi.Amber, ListenerUi.AmberSoft, ListenerGlyph.Internet, "Conectar a internet", DashboardAction.Internet);
        else if (good)
            SetDashboardState("Todo está funcionando", "Recibimos los avisos de BBVA y la conexión con la web está comprobada.", ListenerUi.Green, ListenerUi.GreenSoft, ListenerGlyph.Check, "Comprobar ahora", DashboardAction.Check);
        else if (_webState == WebState.Unavailable)
            SetDashboardState("Esperando a la web", "Los avisos captados siguen guardados mientras recuperamos la conexión con la web.", ListenerUi.Amber, ListenerUi.AmberSoft, ListenerGlyph.Cloud, "Volver a comprobar", DashboardAction.Check);
        else
            SetDashboardState("Comprobando conexión", "Estamos revisando si la web reconoce este celular.", ListenerUi.Brand, ListenerUi.BrandSoft, ListenerGlyph.Refresh, "Comprobar ahora", DashboardAction.Check);
        _stateChecked.Text = _lastCheck == default ? "Conexión con la web aún sin comprobar" : $"Comprobación de la web: {_lastCheck.ToLocalTime():HH:mm:ss}";
        _homeButton.Enabled = !_linking && !(_checking && _dashboardAction == DashboardAction.Check);
        _checkButton.Enabled = !_checking && !_linking;
        _queueTitle.Text = pending == 0 ? "Sin avisos pendientes de envío" : pending == 1 ? "1 aviso por enviar" : $"{pending} avisos por enviar";
        _queueMessage.Text = pending == 0 ? "Puedes consultar los avisos recibidos en Historial."
            : !settings.IsValid ? "Guardados en este celular. Conéctalo con QR para enviarlos."
            : !internet ? "Los avisos pendientes de reintento se enviarán cuando vuelva internet."
            : "Guardados en este celular. Revisa el estado de cada envío en Historial.";
        _queueIcon.Show(pending == 0 ? ListenerGlyph.Check : ListenerGlyph.Cloud, pending == 0 ? ListenerUi.Green : ListenerUi.Amber);
        var power = (PowerManager)GetSystemService(PowerService)!;
        _setupHint.Text = (power.IsIgnoringBatteryOptimizations(PackageName!) ? "Batería: sin restricciones."
            : "Recomendado: permite funcionar sin restricciones de batería.")
            + "\n" + (ListenerNotifications.Allowed(this) ? "Los avisos de Listener están permitidos." : "Falta permitir los avisos de Listener.");
    }

    private void SetDashboardState(string title, string message, Color color, Color background, ListenerGlyph glyph, string action, DashboardAction kind)
    {
        _dashboardAction = kind;
        var signature = $"{title}|{message}|{action}";
        if (_stateSignature == signature) return;
        _stateSignature = signature;
        _stateTitle.Text = title; _stateMessage.Text = message;
        _stateIcon.Show(glyph, color); _stateIcon.Background = ListenerUi.Shape(this, background, 28);
        _homeButton.Text = action;
    }

    private void UpdateLatestNotice(BbvaHistoryEntry? entry)
    {
        _latest = entry;
        if (entry is null) return;
        _lastName.Text = entry.Payment.NombreCompleto ?? entry.Notification.Title ?? "Aviso de BBVA Empresas";
        _lastAmount.Text = entry.Payment.Monto is null ? "Aviso informativo" : Money(entry.Payment.Monto);
        _lastDate.Text = entry.Notification.PostedAt.ToLocalTime().ToString("dd/MM/yyyy · HH:mm:ss");
        _lastState.Text = StatusLabel(entry.Status);
        _lastState.SetTextColor(entry.Status == BbvaDeliveryStatus.StoredByListener ? ListenerUi.Green
            : entry.Status == BbvaDeliveryStatus.HistoryOnly ? ListenerUi.Muted : ListenerUi.Amber);
        _lastNotice.ContentDescription = $"{_lastName.Text}, {_lastAmount.Text}, {_lastState.Text}. Ver detalle del aviso.";
    }

    private async Task RunDashboardActionAsync()
    {
        switch (_dashboardAction)
        {
            case DashboardAction.Link: SelectPage(2); _openConfiguration?.Invoke(); await LinkAsync(); break;
            case DashboardAction.Permission: StartActivity(new Intent(Settings.ActionNotificationListenerSettings)); break;
            case DashboardAction.Reactivate: ReactivateReading(); break;
            case DashboardAction.Repair: ShowReadingRepair(); break;
            case DashboardAction.Internet: StartActivity(new Intent(Settings.ActionWirelessSettings)); break;
            default: await TestConnectionAsync(_homeButton); break;
        }
    }

    private void CheckWebIfNeeded()
    {
        // The existing connection test is read-only here and runs only while the UI is visible.
        // Notification capture, upload workers, credentials and their retry behavior are unchanged.
        if (_screenVisible && !_checking && !_linking && DateTimeOffset.UtcNow >= _nextCheck && HasInternet() && ListenerConfig.Load(this).IsValid)
            _ = CheckWebAsync(false);
    }

    private async Task CheckWebAsync(bool explicitRequest)
    {
        if (_checking || IsFinishing || IsDestroyed) return;
        var settings = ListenerConfig.Load(this);
        if (!settings.IsValid) { _connectionResult!.Text = "Primero conecta este celular escaneando el QR de la web."; return; }
        if (!HasInternet()) { _connectionResult!.Text = "Activa internet para comprobar la conexión. Los avisos captados siguen guardados."; UpdateStatus(); return; }
        _checking = true;
        if (_webState != WebState.Available || DateTimeOffset.UtcNow - _lastCheck >= TimeSpan.FromMinutes(1))
            _webState = WebState.Checking;
        _checkedEndpoint = settings.Endpoint; _checkedCredential = settings.ApiKey;
        _nextCheck = DateTimeOffset.UtcNow.AddSeconds(30);
        var token = _refreshCancellation?.Token ?? default;
        _connectionResult!.Text = "Comprobando la conexión con la web…"; UpdateStatus();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{settings.Endpoint}/api/v1/bbva/connection");
            request.Headers.Add("X-Listener-Api-Key", settings.ApiKey);
            using var response = await ConnectionClient.SendAsync(request, token);
            if (IsFinishing || IsDestroyed) return;
            var current = ListenerConfig.Load(this);
            if (current.Endpoint != settings.Endpoint || current.ApiKey != settings.ApiKey)
            {
                _webState = WebState.Unknown; _nextCheck = DateTimeOffset.MinValue;
                _connectionResult.Text = "La vinculación cambió. Volveremos a comprobarla."; return;
            }
            _lastCheck = DateTimeOffset.UtcNow;
            _technicalConnection.Text = $"Prueba de conexión: HTTP {(int)response.StatusCode}, {_lastCheck.ToLocalTime():dd/MM/yyyy HH:mm:ss}.";
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _webState = WebState.NeedsPairing;
                // Only the existing manual test may clear rejected credentials. Display checks never do.
                if (explicitRequest) ListenerConfig.ClearPairing(this, settings.ApiKey);
                _connectionResult.Text = "La web ya no reconoce este celular. Genera un nuevo QR y vuelve a conectarlo.";
            }
            else if (response.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                var body = json.RootElement;
                var valid = body.ValueKind == JsonValueKind.Object
                    && body.TryGetProperty("conectado", out var connected) && connected.ValueKind == JsonValueKind.True
                    && body.TryGetProperty("servicio", out var service) && service.GetString() == "Listener"
                    && body.TryGetProperty("origen", out var source) && source.GetString() == "BBVA_EMPRESAS";
                _webState = valid ? WebState.Available : WebState.Unavailable;
                _connectionResult.Text = valid ? "Conexión comprobada. La web reconoce este celular."
                    : "La respuesta de la web no permite confirmar la conexión. Vuelve a intentarlo.";
            }
            else
            {
                _webState = WebState.Unavailable;
                _connectionResult.Text = "La web no pudo confirmar la conexión. Los avisos captados siguen guardados en el celular.";
            }
            if (explicitRequest) ListenerDebugLog.Info(this, $"Prueba de conexión BBVA: HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _webState = WebState.Unknown; _nextCheck = DateTimeOffset.MinValue;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            _webState = WebState.Unavailable; _lastCheck = DateTimeOffset.UtcNow;
            _connectionResult.Text = "No pudimos comprobar la conexión con la web. Los avisos captados siguen guardados.";
            _technicalConnection.Text = $"Sin confirmación: {exception.GetType().Name}: {exception.Message}";
            if (explicitRequest) ListenerDebugLog.Warning(this, $"Prueba de conexión: {exception.GetType().Name}.");
        }
        finally
        {
            _checking = false;
            if (!IsFinishing && !IsDestroyed) UpdateStatus();
        }
    }

    private void ShowFriendlyNotification(BbvaHistoryEntry entry)
    {
        var notification = entry.Notification;
        var content = ListenerUi.Column(this);
        content.SetPadding(Dp(20), Dp(12), Dp(20), Dp(12));
        var name = Label(entry.Payment.NombreCompleto ?? notification.Title ?? "Aviso de BBVA", 18, true); name.SetTextIsSelectable(true);
        content.AddView(name);
        ListenerUi.Add(content, Label(entry.Payment.Monto is null ? "Aviso informativo" : Money(entry.Payment.Monto), 26, true), 10);
        ListenerUi.Add(content, Label(StatusLabel(entry.Status), 15, true), 10);
        ListenerUi.Add(content, Label($"Fecha del aviso: {notification.PostedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss}", 13), 8);
        ListenerUi.Add(content, ListenerUi.Text(this, "Recibido en la web confirma la recepción del aviso, no la acreditación de saldo.", 13, color: ListenerUi.Muted), 10, 17);
        var original = Label($"Título: {notification.Title ?? "(vacío)"}\n\nMensaje:\n{notification.Text ?? "(vacío)"}\n\nMensaje ampliado:\n{notification.BigText ?? "(vacío)"}", 14);
        original.SetTextIsSelectable(true);
        AddExpandable(content, "Mensaje original de BBVA", original, false);
        var technical = Label($"Guardado en el celular: {entry.CapturedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss}\n\nDetalle de envío: {entry.Detail}\n\nIntentos de envío: {entry.Attempts}\nMotivo: {entry.Payment.CodigoMotivo}\nEvento: {notification.NotificationKey}", 13);
        technical.SetTextIsSelectable(true);
        AddExpandable(content, "Detalles técnicos del envío", technical, false);
        var scroll = new ScrollView(this); scroll.AddView(content);
        new AlertDialog.Builder(this)!.SetTitle("Detalle del aviso")!.SetView(scroll)!.SetPositiveButton("Cerrar", (_, _) => { })!.Show();
    }
}
