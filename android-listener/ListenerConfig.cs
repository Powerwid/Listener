using Android.Content;

namespace TonysGo.YapeListener.Android;

internal sealed record ListenerSettings(string Endpoint, string DeviceId, string ApiKey)
{
    public bool IsValid
    {
        get
        {
            if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)) return false;
            var allowed = uri.Scheme == Uri.UriSchemeHttps;
#if DEBUG
            allowed |= uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
#endif
            return allowed && !string.IsNullOrWhiteSpace(DeviceId) && ApiKey is { Length: 80 }
                && ApiKey.StartsWith("dvc_", StringComparison.Ordinal);
        }
    }
}

internal static class ListenerConfig
{
    private static readonly object Gate = new();
    private const string PreferencesName = "listener_config";
    private const string DefaultEndpoint = "https://tonysgo-001-site4.ftempurl.com";

    public static string InstallationId(Context context)
    {
        lock (Gate)
        {
            var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
            var value = preferences.GetString("installation_id", null);
            if (!string.IsNullOrEmpty(value)) return value;
            value = Guid.NewGuid().ToString("N");
            if (!preferences.Edit()!.PutString("installation_id", value)!.Commit()) throw new IOException("No se pudo identificar esta instalacion.");
            return value;
        }
    }

    public static ListenerSettings Load(Context context)
    {
        lock (Gate)
        {
            var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
            var deviceId = preferences.GetString("device", null);
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                deviceId = InstallationId(context);
                if (!preferences.Edit()!.PutString("device", deviceId)!.Commit()) throw new IOException("No se pudo guardar el identificador interno.");
            }
            // Existing notification identities stay stable so an upgrade cannot duplicate a queued payment.
            return new(preferences.GetString("endpoint", DefaultEndpoint) ?? DefaultEndpoint,
                deviceId, DeviceCredentialVault.Read(context));
        }
    }

    public static void SavePairing(Context context, string endpoint, string credential)
    {
        lock (Gate)
        {
            DeviceCredentialVault.Save(context, credential);
            var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
            if (!preferences.Edit()!.PutString("endpoint", endpoint.TrimEnd('/'))!.Commit())
            {
                DeviceCredentialVault.Clear(context);
                throw new IOException("No se pudo guardar el servidor de la vinculacion.");
            }
        }
        SetLastStatus(context, "Telefono vinculado mediante QR. Los abonos pendientes se enviaran automaticamente.");
    }

    public static bool ClearPairing(Context context, string rejectedCredential)
    {
        lock (Gate)
        {
            // A delayed 401 from an old upload must not erase a freshly scanned connection.
            if (DeviceCredentialVault.Read(context) != rejectedCredential) return false;
            DeviceCredentialVault.Clear(context);
        }
        SetLastStatus(context, "La vinculacion ya no esta activa. Escanea un nuevo QR desde Conexion.");
        return true;
    }

    public static string LastStatus(Context context) => context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
        ?.GetString("last_status", "Sin notificaciones capturadas todavia.") ?? "Sin notificaciones capturadas todavia.";

    public static void SetLastStatus(Context context, string status) => context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
        ?.Edit()?.PutString("last_status", status)?.Apply();
}
