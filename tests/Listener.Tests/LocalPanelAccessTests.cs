using System.Net;
using Listener.Api.Email;
using Listener.Api.Panel;

namespace Listener.Tests;

public class LocalPanelAccessTests
{
    [Theory]
    [InlineData("127.0.0.1", "localhost", true)]
    [InlineData("::1", "localhost", true)]
    [InlineData("::ffff:127.0.0.1", "localhost", true)]
    [InlineData("127.0.0.1", "127.0.0.1", true)]
    [InlineData("::1", "[::1]", true)]
    [InlineData("192.168.1.2", "localhost", false)]
    [InlineData("127.0.0.1", "other.example", false)]
    [InlineData("127.0.0.1", "localhost.evil.example", false)]
    [InlineData("127.0.0.1", "192.168.1.1", false)]
    public void OnlyLocalClientAndLocalHostAreAllowed(string remote, string host, bool expected)
    {
        Assert.Equal(expected, LocalPanelAccess.IsAllowed(IPAddress.Parse(remote), host));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(91, 20)]
    [InlineData(30, 0)]
    [InlineData(30, 101)]
    public async Task InvalidReadLimitsFailBeforeConnecting(int days, int limit)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            GmailBcpReader.ReadReportAsync("sample@gmail.com", "abcdefghijklmnop", days, limit, CancellationToken.None));
    }
}
