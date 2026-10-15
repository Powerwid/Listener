using Android.Content;

namespace TonysGo.YapeListener.Android;

internal sealed record ListenerSettings(string Endpoint, string DeviceId, string ApiKey)
{
    public bool IsValid
    {
        get
        {
            if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var allowedEndpoint = uri.Scheme == Uri.UriSchemeHttps;
#if DEBUG
            allowedEndpoint |= uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
#endif

            return allowedEndpoint &&
                   !string.IsNullOrWhiteSpace(DeviceId) &&
                   ApiKey.Length >= 32;
        }
    }
}

internal static class ListenerConfig
{
    private const string PreferencesName = "listener_config";
    private const string EndpointKey = "endpoint";
    private const string DeviceKey = "device";
    private const string ApiKeyKey = "api_key";
    private const string LastStatusKey = "last_status";

    // Published Listener used until the device saves its own values.
    private const string DefaultEndpoint = "https://tonysgo-001-site4.ftempurl.com";
    private const string DefaultDeviceId = "yape-caja-01";

    public static ListenerSettings Load(Context context)
    {
        var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
        return new ListenerSettings(
            preferences?.GetString(EndpointKey, DefaultEndpoint) ?? DefaultEndpoint,
            preferences?.GetString(DeviceKey, DefaultDeviceId) ?? DefaultDeviceId,
            preferences?.GetString(ApiKeyKey, string.Empty) ?? string.Empty);
    }

    public static void Save(Context context, ListenerSettings settings)
    {
        var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
            ?? throw new InvalidOperationException("No se pudo abrir la configuracion local.");

        using var editor = preferences.Edit();
        editor?.PutString(EndpointKey, settings.Endpoint.Trim().TrimEnd('/'));
        editor?.PutString(DeviceKey, settings.DeviceId.Trim());
        editor?.PutString(ApiKeyKey, settings.ApiKey.Trim());
        editor?.Apply();
    }

    public static string LastStatus(Context context)
    {
        var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
        return preferences?.GetString(LastStatusKey, "Sin notificaciones capturadas todavía.")
               ?? "Sin notificaciones capturadas todavía.";
    }

    public static void SetLastStatus(Context context, string status)
    {
        var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
        using var editor = preferences?.Edit();
        editor?.PutString(LastStatusKey, status.Length <= 500 ? status : status[..500]);
        editor?.Apply();
    }
}
