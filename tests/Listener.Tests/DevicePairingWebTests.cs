using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Listener.Api.Devices;
using Listener.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TonysGo.Listener.Pairing;

namespace Listener.Tests;

public class DevicePairingWebTests
{
    [Fact]
    public async Task AdministrationRequiresLoginAndCsrfWhilePairingRequiresAValidOneUseQr()
    {
        await using var factory = new PairingWebFactory();
        using var browser = factory.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsync("/api/v1/admin/devices/pairings", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/dispositivos")).StatusCode);
        var csrf = await Login(browser);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/api/v1/admin/devices/pairings", null)).StatusCode);
        browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        using var create = await browser.PostAsync("/api/v1/admin/devices/pairings", null);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        using var image = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var bytes = Convert.FromBase64String(image.RootElement.GetProperty("qrImage").GetString()!.Split(',')[1]);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        Assert.False(image.RootElement.TryGetProperty("credential", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/v1/devices/pair", new DevicePairingRequest(
            new string('x', 43), Guid.NewGuid().ToString("N"), "phone", "Android"))).StatusCode);
    }

    [Fact]
    public async Task PairedPhoneCanConnectAndRevocationTakesEffectImmediately()
    {
        await using var factory = new PairingWebFactory();
        var browser = factory.CreateClient(new() { AllowAutoRedirect = false });
        var csrf = await Login(browser);
        browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        DevicePairingQr qr;
        using (var scope = factory.Services.CreateScope())
            qr = await scope.ServiceProvider.GetRequiredService<DevicePairingService>().CreateAsync("https://listener.example", "admin", default);
        using var phone = factory.CreateClient(new() { HandleCookies = false });
        using var paired = await phone.PostAsJsonAsync("/api/v1/devices/pair", new DevicePairingRequest(
            qr.Token, Guid.NewGuid().ToString("N"), "phone-id", "Android de prueba"));
        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        var credential = (await paired.Content.ReadFromJsonAsync<DevicePairingResponse>())!.Credential;
        phone.DefaultRequestHeaders.Add("X-Listener-Api-Key", credential);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/v1/bbva/connection")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.PostAsJsonAsync("/api/v1/bbva/notifications",
            BbvaNotificationTests.Notice() with { DeviceId = "another-phone" })).StatusCode);
        using var list = JsonDocument.Parse(await browser.GetStringAsync("/api/v1/admin/devices"));
        var device = list.RootElement[0];
        Assert.False(device.TryGetProperty("credentialHash", out _));
        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync($"/api/v1/admin/devices/{device.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/v1/bbva/connection")).StatusCode);
    }

    [Fact]
    public async Task WrongPasswordDoesNotCreateAnAdministrativeSession()
    {
        await using var factory = new PairingWebFactory();
        using var browser = factory.CreateClient(new() { AllowAutoRedirect = false });
        var csrf = Token(await browser.GetStringAsync("/acceso"));
        using var response = await browser.PostAsync("/acceso", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "admin", ["Password"] = "incorrect-password", ["__RequestVerificationToken"] = csrf
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("incorrect-password", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/admin/devices")).StatusCode);
    }

    [Fact]
    public async Task LoginEndpointLimitsRepeatedAttempts()
    {
        await using var factory = new PairingWebFactory();
        using var browser = factory.CreateClient(new() { AllowAutoRedirect = false });
        for (var attempt = 0; attempt < 10; attempt++)
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/acceso")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await browser.GetAsync("/acceso")).StatusCode);
    }

    private static async Task<string> Login(HttpClient browser)
    {
        var token = Token(await browser.GetStringAsync("/acceso"));
        using var response = await browser.PostAsync("/acceso", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "admin", ["Password"] = PairingWebFactory.Password, ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Token(await browser.GetStringAsync("/dispositivos"));
    }
    private static string Token(string html)
    {
        var input = Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*>").Value;
        return Regex.Match(input, "value=\"([^\"]+)\"").Groups[1].Value;
    }

    private sealed class PairingWebFactory : WebApplicationFactory<Program>
    {
        public const string Password = "test-administrator-password-2026";
        private static readonly string PasswordHash = AdminCredentials.HashPassword(Password);
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly string _root = Path.Combine(Path.GetTempPath(), "listener-web-tests-" + Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(_root);
            _connection.Open();
            builder.UseEnvironment("Development").UseContentRoot(_root);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ListenerDb"] = "Server=localhost;Database=NotUsedByTheseTests;Integrated Security=True",
                ["ListenerAdmin:Username"] = "admin", ["ListenerAdmin:PasswordHash"] = PasswordHash,
                ["DevicePairing:PublicBaseUrl"] = "https://listener.example"
            }));
            builder.ConfigureTestServices(services =>
            {
                foreach (var worker in services.Where(x => x.ServiceType == typeof(IHostedService)
                    && x.ImplementationType?.Namespace?.StartsWith("Listener.Api", StringComparison.Ordinal) == true).ToArray()) services.Remove(worker);
                services.RemoveAll<DbContextOptions<PairingDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<PairingDbContext>>();
                services.AddDbContext<PairingDbContext>(options => options.UseSqlite(_connection));
            });
        }
        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);
            using var scope = host.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<PairingDbContext>().Database.EnsureCreated();
            return host;
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _connection.DisposeAsync();
            // Only files created under this test's unique, explicit temporary directory.
            if (Directory.Exists(_root))
            {
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.Delete(file);
                foreach (var directory in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length)) Directory.Delete(directory);
                Directory.Delete(_root);
            }
        }
    }
}
