using Listener.Api.Devices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TonysGo.Listener.Pairing;

namespace Listener.Tests;

public sealed class DevicePairingTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TestClock _clock = new();
    private PairingDbContext _db = null!;
    private DevicePairingService _service = null!;
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new PairingDbContext(new DbContextOptionsBuilder<PairingDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _service = new(_db, _clock);
    }
    public async Task DisposeAsync() { await _db.DisposeAsync(); await _connection.DisposeAsync(); }
    private static DevicePairingRequest Request(DevicePairingQr qr, string? installation = null) =>
        new(qr.Token, installation ?? Guid.NewGuid().ToString("N"), "internal-phone-id", "Android de prueba");

    [Fact]
    public async Task QrStoresOnlyAHashAndIsConsumedOnce()
    {
        var qr = await _service.CreateAsync("https://listener.example", "admin", default);
        var ticket = await _db.Tickets.AsNoTracking().SingleAsync();
        Assert.Equal(_clock.GetUtcNow().AddMinutes(5), qr.ExpiresAt);
        Assert.Equal(DevicePairingService.Hash(qr.Token), ticket.TokenHash);
        Assert.NotEqual(qr.Token, ticket.TokenHash);
        var result = await _service.RedeemAsync(Request(qr), default);
        Assert.Equal(80, result.Credential.Length);
        var device = await _service.AuthenticateAsync(result.Credential, default);
        Assert.NotNull(device);
        Assert.Equal("internal-phone-id", device.NotificationDeviceId);
        Assert.NotEqual(result.Credential, device.CredentialHash);
        await Assert.ThrowsAsync<DevicePairingException>(() => _service.RedeemAsync(Request(qr), default));
        Assert.Single(await _db.Devices.ToArrayAsync());
        Assert.Null(await _service.AuthenticateAsync(result.Credential[..^1] + "!", default));
        Assert.Null(await _service.AuthenticateAsync(new string('k', 32), default));
    }

    [Fact]
    public async Task ExpiredQrDoesNotRegisterADevice()
    {
        var qr = await _service.CreateAsync("https://listener.example", "admin", default);
        _clock.Now = _clock.Now.AddMinutes(5);
        await Assert.ThrowsAsync<DevicePairingException>(() => _service.RedeemAsync(Request(qr), default));
        Assert.Empty(await _db.Devices.ToArrayAsync());
    }

    [Fact]
    public async Task GeneratingAnotherQrInvalidatesThePreviousOne()
    {
        var oldQr = await _service.CreateAsync("https://listener.example", "admin", default);
        var newQr = await _service.CreateAsync("https://listener.example", "admin", default);
        await Assert.ThrowsAsync<DevicePairingException>(() => _service.RedeemAsync(Request(oldQr), default));
        Assert.NotNull(await _service.RedeemAsync(Request(newQr), default));
    }

    [Fact]
    public async Task RevocationAndRelinkingInvalidateOldCredentialsWithoutChangingNotificationIdentity()
    {
        var installation = Guid.NewGuid().ToString("N");
        var qr = await _service.CreateAsync("https://listener.example", "admin", default);
        var first = await _service.RedeemAsync(Request(qr, installation), default);
        var device = (await _service.AuthenticateAsync(first.Credential, default))!;
        Assert.True(await _service.RevokeAsync(device.Id, default));
        Assert.Null(await _service.AuthenticateAsync(first.Credential, default));
        var next = await _service.CreateAsync("https://listener.example", "admin", default);
        var second = await _service.RedeemAsync(Request(next, installation), default);
        Assert.NotEqual(first.Credential, second.Credential);
        Assert.Equal(first.NotificationDeviceId, second.NotificationDeviceId);
        Assert.NotNull(await _service.AuthenticateAsync(second.Credential, default));
        Assert.Null(await _service.AuthenticateAsync(first.Credential, default));
        Assert.Single(await _service.ListAsync(default));
    }

    [Fact]
    public async Task InvalidDeviceRequestDoesNotConsumeTheQr()
    {
        var qr = await _service.CreateAsync("https://listener.example", "admin", default);
        await Assert.ThrowsAsync<DevicePairingException>(() => _service.RedeemAsync(Request(qr) with { InstallationId = "manual-name" }, default));
        Assert.NotNull(await _service.RedeemAsync(Request(qr), default));
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

public class DevicePairingProtocolTests
{
    [Theory]
    [InlineData("https://listener.example", false, true)]
    [InlineData("http://127.0.0.1:5057", true, true)]
    [InlineData("http://localhost:5057", true, true)]
    [InlineData("http://localhost:5057", false, false)]
    [InlineData("http://listener.example", true, false)]
    [InlineData("https://admin:password@listener.example", false, false)]
    [InlineData("https://listener.example/?token=secret", false, false)]
    [InlineData("https://listener.example/#token", false, false)]
    [InlineData("javascript:alert(1)", true, false)]
    public void OnlyAcceptsSupportedServerAddresses(string url, bool local, bool expected) =>
        Assert.Equal(expected, DevicePairingProtocol.IsServerUrl(url, local));

    [Fact]
    public void QrContractRoundTripsAndRejectsExpiredOrUnrelatedContent()
    {
        var now = DateTimeOffset.UtcNow;
        var qr = new DevicePairingQr(DevicePairingProtocol.QrType, 1, "https://listener.example", new string('a', 43), now.AddMinutes(5));
        var json = System.Text.Json.JsonSerializer.Serialize(qr, DevicePairingJsonContext.Default.DevicePairingQr);
        Assert.Equal(qr, DevicePairingProtocol.Parse(json, false, now));
        Assert.Throws<FormatException>(() => DevicePairingProtocol.Parse(json, false, now.AddMinutes(6)));
        Assert.Throws<FormatException>(() => DevicePairingProtocol.Parse("https://example.com", false, now));
        Assert.Throws<FormatException>(() => DevicePairingProtocol.Parse("{}", false, now));
    }
}
