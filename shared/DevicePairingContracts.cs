using System.Text.Json;
using System.Text.Json.Serialization;

namespace TonysGo.Listener.Pairing;

public sealed record DevicePairingQr(string Type, int Version, string ServerUrl, string Token, DateTimeOffset ExpiresAt);
public sealed record DevicePairingRequest(string Token, string InstallationId, string NotificationDeviceId, string DeviceModel);
public sealed record DevicePairingResponse(string Credential, string NotificationDeviceId);
public sealed record DevicePairingError(string Error);

public static class DevicePairingProtocol
{
    public const string QrType = "tonysgo-listener-pairing";

    public static DevicePairingQr Parse(string json, bool allowLocalHttp, DateTimeOffset now)
    {
        if (json.Length > 4096) throw new FormatException("El QR no es una vinculacion de Listener.");
        DevicePairingQr qr;
        try { qr = JsonSerializer.Deserialize(json, DevicePairingJsonContext.Default.DevicePairingQr)
                ?? throw new FormatException("El QR no contiene datos."); }
        catch (JsonException) { throw new FormatException("Escanea el QR generado en la web de Listener."); }
        if (qr.Type != QrType || qr.Version != 1 || !IsServerUrl(qr.ServerUrl, allowLocalHttp)
            || qr.Token is not { Length: 43 } || qr.Token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new FormatException("Escanea el QR generado en la web de Listener.");
        if (qr.ExpiresAt <= now) throw new FormatException("El QR vencio. Genera otro en la web.");
        return qr with { ServerUrl = qr.ServerUrl.TrimEnd('/') };
    }

    public static bool IsServerUrl(string? value, bool allowLocalHttp) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && uri.AbsolutePath == "/" && (uri.Scheme == Uri.UriSchemeHttps
            || allowLocalHttp && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DevicePairingQr))]
[JsonSerializable(typeof(DevicePairingRequest))]
[JsonSerializable(typeof(DevicePairingResponse))]
[JsonSerializable(typeof(DevicePairingError))]
public partial class DevicePairingJsonContext : JsonSerializerContext { }
