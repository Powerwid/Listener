using System.Security.Cryptography;
using System.Text;

namespace Listener.Api.Security;

public enum ApiClient
{
    AndroidCollector,
    TonysGo
}

public sealed class ApiKeyValidator(IConfiguration configuration)
{
    public const string HeaderName = "X-Listener-Api-Key";

    public bool IsConfigured(ApiClient client) => GetExpected(client) is { Length: >= 32 };

    public bool IsValid(ApiClient client, string? supplied)
    {
        var expected = GetExpected(client);
        if (expected is not { Length: >= 32 } || string.IsNullOrEmpty(supplied))
            return false;

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }

    private string? GetExpected(ApiClient client) => configuration[
        client == ApiClient.AndroidCollector
            ? "ListenerSecurity:CollectorApiKey"
            : "ListenerSecurity:TonysGoApiKey"];
}
