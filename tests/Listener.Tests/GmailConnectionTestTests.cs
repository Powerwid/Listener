using Listener.Api.Diagnostics;

namespace Listener.Tests;

public class GmailConnectionTestTests
{
    [Theory]
    [InlineData("sample@gmail.com", true)]
    [InlineData("sample@GMAIL.COM", true)]
    [InlineData("sample@googlemail.com", true)]
    [InlineData("sample@live.com", false)]
    [InlineData("sample@gmail.com.evil.example", false)]
    [InlineData("Name <sample@gmail.com>", false)]
    [InlineData("sample@gmail.com\r\n", false)]
    [InlineData("@gmail.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AllowsOnlyPlainPersonalGmailAddresses(string? input, bool expected)
    {
        Assert.Equal(expected, GmailConnectionTest.IsPersonalGmailAddress(input));
    }

    [Fact]
    public void RemovesDisplaySpacesFromAppPassword()
    {
        // Synthetic fixture, never an actual account credential.
        var normalized = GmailConnectionTest.NormalizeAppPassword("abcd efgh ijkl mnop");
        Assert.Equal("abcdefghijklmnop", normalized);
        Assert.True(GmailConnectionTest.HasAppPasswordFormat(normalized));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("abcdefghijklmnopq")]
    [InlineData("abcdefghijklmno!")]
    public void RejectsMalformedAppPasswords(string value)
    {
        Assert.False(GmailConnectionTest.HasAppPasswordFormat(value));
    }

    [Fact]
    public async Task CancellationStopsBeforeNetworkAccess()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GmailConnectionTest.CheckInboxAsync("sample@gmail.com", "abcdefghijklmnop", cancelled.Token));
    }

    [Fact]
    public async Task InvalidConfigurationStopsBeforeNetworkAccess()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            GmailConnectionTest.CheckInboxAsync("sample@live.com", "abcdefghijklmnop", CancellationToken.None));
    }

    [Fact]
    public void ErrorMessagesNeverEchoExceptionDetails()
    {
        const string sensitiveFixture = "private-server-response";
        Exception[] exceptions =
        [
            new MailKit.Security.AuthenticationException(sensitiveFixture),
            new IOException(sensitiveFixture),
            new OperationCanceledException(sensitiveFixture),
            new InvalidOperationException(sensitiveFixture)
        ];
        foreach (var exception in exceptions)
            Assert.DoesNotContain(sensitiveFixture, GmailConnectionTest.GetSafeError(exception));
    }
}
