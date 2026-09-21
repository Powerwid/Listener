using System.Net;

namespace Listener.Api.Panel;

public static class LocalPanelAccess
{
    public static bool IsAllowed(IPAddress? remote, string host)
    {
        if (remote is null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (!IPAddress.IsLoopback(remote)) return false;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
    }
}
