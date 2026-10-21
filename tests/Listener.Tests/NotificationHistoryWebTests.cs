using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Listener.Api.Bbva;
using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Listener.Api.Devices;
using Listener.Api.History;
using Listener.Api.Security;
using Listener.Api.Yape;
using Listener.Api.Email;
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
using TonysGo.Listener.Bbva;

namespace Listener.Tests;

public class NotificationHistoryWebTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task HistoryRequiresLoginAndEmailReaderIsRemovedInEveryEnvironment(string environment)
    {
        await using var factory = new HistoryWebFactory(environment);
        using var browser = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        foreach (var path in new[] { "/historial", "/dispositivos" })
            Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync(path)).StatusCode);
        foreach (var path in new[] { "/api/v1/admin/history" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(path)).StatusCode);
        foreach (var path in new[] { "/panel", "/api/notificaciones", "/api/notificaciones/yape", "/api/lector", "/panel/panel.js" })
            Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(path)).StatusCode);
        await Login(browser);
        foreach (var path in new[] { "/historial", "/dispositivos", "/api/v1/admin/history" })
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(path)).StatusCode);
        Assert.DoesNotContain("AutomaticReaderWorker", factory.ApplicationWorkers);
        foreach (var path in new[] { "/historial", "/dispositivos" })
            Assert.DoesNotContain("Lector de correo", await browser.GetStringAsync(path));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync("/panel?handler=Gmail", null)).StatusCode);
        var response = await browser.GetAsync("/api/v1/admin/history");
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task AllStoredSourcesArePaginatedSearchableAndFilteredByPeruDateWithoutExposingSimulation()
    {
        await using var factory = new HistoryWebFactory();
        using var browser = factory.CreateClient(new() { AllowAutoRedirect = false });
        await Login(browser);
        var all = (await browser.GetFromJsonAsync<HistoryPage>("/api/v1/admin/history"))!;
        Assert.Equal(29, all.Summary.Total);
        Assert.Equal(2, all.Pages);
        Assert.Equal(25, all.Items.Count);
        Assert.Equal(27, all.Summary.BbvaPayments);
        Assert.Equal(26, all.Summary.Pending);
        Assert.Equal(1, all.Summary.Sent);
        var second = (await browser.GetFromJsonAsync<HistoryPage>("/api/v1/admin/history?page=2"))!;
        Assert.Equal(4, second.Items.Count);
        Assert.Empty(second.Items.Select(x => x.Id).Intersect(all.Items.Select(x => x.Id)));
        var yape = (await browser.GetFromJsonAsync<HistoryPage>("/api/v1/admin/history?source=yape"))!;
        Assert.Single(yape.Items); Assert.Equal("Yape", yape.Items[0].Source);
        Assert.Equal("José MUÑOZ PÉREZ", yape.Items[0].Name);
        var accented = (await browser.GetFromJsonAsync<HistoryPage>("/api/v1/admin/history?search=jos%C3%A9%20mu%C3%B1oz"))!;
        Assert.Single(accented.Items); Assert.Equal("Yape", accented.Items[0].Source);
        var mail = (await browser.GetFromJsonAsync<HistoryPage>("/api/v1/admin/history?source=correo"))!;
        Assert.Single(mail.Items); Assert.Equal("CORREO ANTERIOR", mail.Items[0].Name);
        var found = (await browser.GetFromJsonAsync<HistoryPage>("/api/v1/admin/history?search=persona%20especial"))!;
        Assert.Single(found.Items); Assert.Equal(12.50m, found.Items[0].Amount);
        var peru = (await browser.GetFromJsonAsync<HistoryPage>("/api/v1/admin/history?from=2026-10-20&to=2026-10-20"))!;
        Assert.Single(peru.Items); Assert.Equal(HistoryWebFactory.PendingId, peru.Items[0].Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync("/api/v1/admin/history?from=2026-10-21&to=2026-10-20")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync("/api/v1/admin/history?source=unexpected")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"/api/v1/admin/history/{HistoryWebFactory.SimulationId}")).StatusCode);
    }

    [Fact]
    public async Task DetailsShowOriginalMessageAndDeliveryStatusWithoutChangingPayments()
    {
        await using var factory = new HistoryWebFactory();
        using var browser = factory.CreateClient(new() { AllowAutoRedirect = false });
        await Login(browser);
        var detail = (await browser.GetFromJsonAsync<HistoryDetail>($"/api/v1/admin/history/{HistoryWebFactory.PendingId}"))!;
        Assert.Equal("pending", detail.Item.Status);
        Assert.Contains("PERSONA ESPECIAL", detail.Text);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
        Assert.Empty(await db.SolicitudesVerificacion.ToListAsync());
        Assert.Empty(await db.EvidenciasPago.ToListAsync());
    }

    [Fact]
    public async Task FailedAndDeliveredHistoryHaveSeparateFilters()
    {
        await using var factory = new HistoryWebFactory();
        using var browser = factory.CreateClient(new() { AllowAutoRedirect = false });
        await Login(browser);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
            db.EventosAuditoria.Add(new() { CorreoProcesadoId = HistoryWebFactory.PendingId, TipoEvento = BbvaAbonoDispatcher.FailedEvent,
                Actor = "BbvaAbonoWorker", CodigoMotivo = "HTTP_503", IdCorrelacion = HistoryWebFactory.PendingId.ToString() });
            await db.SaveChangesAsync();
        }
        var pending = (await browser.GetFromJsonAsync<HistoryPage>("/api/v1/admin/history?status=failed"))!;
        var item = Assert.Single(pending.Items);
        Assert.Equal("failed", item.Status); Assert.Equal("HTTP_503", item.Reason);
        var sent = (await browser.GetFromJsonAsync<HistoryPage>("/api/v1/admin/history?status=sent"))!;
        Assert.Single(sent.Items); Assert.Equal("sent", sent.Items[0].Status);
    }

    internal static async Task<string> Login(HttpClient browser)
    {
        static string Token(string html) => Regex.Match(Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*>").Value, "value=\"([^\"]+)\"").Groups[1].Value;
        var csrf = Token(await browser.GetStringAsync("/acceso"));
        using var response = await browser.PostAsync("/acceso", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "admin", ["Password"] = HistoryWebFactory.Password, ["__RequestVerificationToken"] = csrf
        }));
        Assert.Equal("/historial", response.Headers.Location?.OriginalString);
        return Token(await browser.GetStringAsync("/historial"));
    }
}

public sealed class HistoryWebFactory(string environment = "Development", string? webRoot = null) : WebApplicationFactory<Program>
{
    public const string Password = "qa-only-history-password";
    public static readonly Guid PendingId = Guid.Parse("12345678-1234-1234-1234-123456789012");
    public static readonly Guid SimulationId = Guid.Parse("99999999-1234-1234-1234-123456789012");
    private static readonly string PasswordHash = AdminCredentials.HashPassword(Password);
    private readonly SqliteConnection _historyConnection = new("Data Source=:memory:");
    private readonly SqliteConnection _pairingConnection = new("Data Source=:memory:");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "listener-history-tests-" + Guid.NewGuid().ToString("N"));
    public string[] ApplicationWorkers { get; private set; } = [];
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_root); _historyConnection.Open(); _pairingConnection.Open();
        builder.UseEnvironment(environment).UseContentRoot(_root);
        if (webRoot is not null) builder.UseWebRoot(webRoot);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ListenerDb"] = "Server=localhost;Database=NotUsedByHistoryTests;Integrated Security=True",
            ["ListenerAdmin:Username"] = "admin", ["ListenerAdmin:PasswordHash"] = PasswordHash,
            ["TonysGo:AbonosUrl"] = "https://tonysgo.example/abonos", ["TonysGo:CallbackApiKey"] = new string('k', 32)
        }));
        builder.ConfigureTestServices(services =>
        {
            ApplicationWorkers = services.Where(x => x.ServiceType == typeof(IHostedService)
                && x.ImplementationType?.Namespace?.StartsWith("Listener.Api", StringComparison.Ordinal) == true)
                .Select(x => x.ImplementationType!.Name).ToArray();
            foreach (var worker in services.Where(x => x.ServiceType == typeof(IHostedService)
                && x.ImplementationType?.Namespace?.StartsWith("Listener.Api", StringComparison.Ordinal) == true).ToArray()) services.Remove(worker);
            services.RemoveAll<DbContextOptions<ListenerDbContext>>(); services.RemoveAll<IDbContextOptionsConfiguration<ListenerDbContext>>();
            services.AddDbContext<ListenerDbContext>(options => options.UseSqlite(_historyConnection).ReplaceService<IModelCustomizer, HistoryTestModel>());
            services.RemoveAll<DbContextOptions<PairingDbContext>>(); services.RemoveAll<IDbContextOptionsConfiguration<PairingDbContext>>();
            services.AddDbContext<PairingDbContext>(options => options.UseSqlite(_pairingConnection));
        });
    }
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ListenerDb"] = "Server=localhost;Database=NotUsedByHistoryTests;Integrated Security=True"
        }));
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<PairingDbContext>().Database.EnsureCreated();
        var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
        db.Database.EnsureCreated(); Seed(db);
        return host;
    }
    public static void Seed(ListenerDbContext db)
    {
        var cursor = new EstadoSincronizacionCorreo { Buzon = "android-bbva:phone-test", Carpeta = "NOTIFICATIONS", UidValidity = 1 };
        var now = new DateTimeOffset(2026, 10, 21, 5, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 28; i++)
        {
            var input = new BbvaNotificationRequest("event-" + i, "phone-test", BbvaNotificationParser.OfficialPackage,
                i == 0 ? now.AddSeconds(-1) : now.AddSeconds(i), "Cobro con QR", (i == 0 ? "PERSONA ESPECIAL" : "PERSONA PRUEBA") + " te hizo un pago de S/ 12.50", null);
            var row = new CorreoProcesado
            {
                Id = i == 0 ? PendingId : i == 27 ? SimulationId : Guid.NewGuid(), EsSimulacion = i == 27,
                EstadoSincronizacionCorreo = cursor, UidValidity = 1, Uid = i + 1, MessageId = input.NotificationKey,
                HashContenido = new string('A', 64), Asunto = input.Title, FechaRecepcion = input.PostedAt,
                EstadoProcesamiento = EstadoProcesamientoCorreo.Procesado, CodigoMotivo = "ABONO_BBVA_EXTRAIDO", VersionExtractor = BbvaNotificationService.Extractor,
                ResultadoJson = JsonSerializer.Serialize(new StoredBbvaNotification(input, BbvaNotificationParser.Parse(input)), BbvaNotificationService.JsonOptions)
            };
            db.CorreosProcesados.Add(row);
            db.EventosAuditoria.Add(new() { CorreoProcesado = row, TipoEvento = "NotificacionBbvaRecibida", Actor = "android:phone-test", CodigoMotivo = row.CodigoMotivo, IdCorrelacion = row.Id.ToString() });
            if (i == 1) db.EventosAuditoria.Add(new() { CorreoProcesado = row, TipoEvento = BbvaAbonoDispatcher.DeliveredEvent, Actor = "BbvaAbonoWorker", CodigoMotivo = "HTTP_2XX", IdCorrelacion = row.Id.ToString() });
        }
        db.CorreosProcesados.Add(new()
        {
            EstadoSincronizacionCorreo = cursor, UidValidity = 1, Uid = 100, HashContenido = new string('B', 64), VersionExtractor = "android-yape-v1",
            FechaRecepcion = now, EstadoProcesamiento = EstadoProcesamientoCorreo.Procesado,
            ResultadoJson = JsonSerializer.Serialize(new YapeNotificationData("yape-old", "old-phone", "yape", now, "Yape recibido", "Aviso anterior", null, true, 5, "PEN", null, "José MUÑOZ PÉREZ", "OLD"), BcpEmailCommand.JsonOptions)
        });
        db.CorreosProcesados.Add(new()
        {
            EstadoSincronizacionCorreo = cursor, UidValidity = 1, Uid = 101, HashContenido = new string('C', 64), VersionExtractor = "bcp-auto-v1",
            FechaRecepcion = now, EstadoProcesamiento = EstadoProcesamientoCorreo.Procesado,
            ResultadoJson = JsonSerializer.Serialize(new BcpEmailResult { Ordenante = "CORREO ANTERIOR", Monto = 10, Moneda = "PEN" }, BcpEmailCommand.JsonOptions)
        });
        db.SaveChanges();
    }
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync(); await _historyConnection.DisposeAsync(); await _pairingConnection.DisposeAsync();
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.Delete(file);
            foreach (var directory in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length)) Directory.Delete(directory);
            Directory.Delete(_root);
        }
    }
}
