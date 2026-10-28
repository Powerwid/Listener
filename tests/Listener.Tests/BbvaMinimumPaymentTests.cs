using System.Net;
using System.Text.Json;
using Listener.Api.Bbva;
using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Listener.Api.History;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TonysGo.Listener.Bbva;

namespace Listener.Tests;

public sealed class BbvaMinimumPaymentTests
{
    [Theory]
    [InlineData("0.01", false, "9.99")]
    [InlineData("9.99", false, "0.01")]
    [InlineData("10.00", true, "0")]
    [InlineData("10.01", true, "0")]
    public async Task SendsOneValidationResultForEachPaymentAtTheTenSolesBoundary(string value, bool correct, string missing)
    {
        await using var host = new PaymentHost();
        var amount = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var receipt = await host.Ingest(amount);
        Assert.True(receipt.EsAbono);
        Assert.Equal(correct, receipt.Validacion!.Correcto);
        Assert.Equal(decimal.Parse(missing, System.Globalization.CultureInfo.InvariantCulture), receipt.Validacion.MontoFaltante);
        Assert.Equal(1, await host.Dispatch());
        Assert.Equal(0, await host.Dispatch());
        var call = Assert.Single(host.Handler.Calls);
        using var json = JsonDocument.Parse(call.Json);
        string[] expectedFields = ["eventoId", "acumulacionId", "secuencia", "nombreCompleto", "correcto",
            "monto", "moneda", "origen", "montoAcumulado", "montoFaltante", "fechaNotificacion"];
        Assert.Equal(expectedFields.Order(StringComparer.Ordinal),
            json.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.Equal(receipt.Id, json.RootElement.GetProperty("eventoId").GetGuid());
        Assert.Equal(receipt.Validacion.AcumulacionId, json.RootElement.GetProperty("acumulacionId").GetGuid());
        Assert.Equal(receipt.Validacion.Secuencia, json.RootElement.GetProperty("secuencia").GetInt64());
        Assert.Equal(receipt.NombreCompleto, json.RootElement.GetProperty("nombreCompleto").GetString());
        Assert.Equal(receipt.Validacion.FechaNotificacion, json.RootElement.GetProperty("fechaNotificacion").GetDateTimeOffset());
        Assert.Equal(PaymentHost.AbonosUrl, call.Url);
        Assert.Equal(correct, json.RootElement.GetProperty("correcto").GetBoolean());
        Assert.Equal(amount, json.RootElement.GetProperty("monto").GetDecimal());
        Assert.Equal("PEN", json.RootElement.GetProperty("moneda").GetString());
        Assert.Equal("BBVA_EMPRESAS", json.RootElement.GetProperty("origen").GetString());
        Assert.Equal(amount, json.RootElement.GetProperty("montoAcumulado").GetDecimal());
        Assert.Equal(receipt.Validacion.MontoFaltante, json.RootElement.GetProperty("montoFaltante").GetDecimal());
        Assert.Equal(new string('k', 32), call.Key);
    }

    [Fact]
    public async Task FiveThenSevenProducesAnInsufficientResponseAndACorrectResponseForTwelve()
    {
        await using var host = new PaymentHost();
        var first = await host.Ingest(5m);
        var second = await host.Ingest(7m);
        Assert.False(first.Validacion!.Correcto);
        Assert.Equal(5m, first.Validacion.MontoFaltante);
        Assert.True(second.Validacion!.Correcto);
        Assert.Equal(12m, second.Validacion.MontoAcumulado);
        Assert.Equal(first.Validacion.AcumulacionId, second.Validacion.AcumulacionId);
        Assert.Equal(1, await host.Dispatch());
        Assert.Equal(1, await host.Dispatch());
        Assert.Equal(0, await host.Dispatch());
        Assert.Equal(2, host.Handler.Calls.Count);
        using var insufficient = JsonDocument.Parse(host.Handler.Calls[0].Json);
        using var correct = JsonDocument.Parse(host.Handler.Calls[1].Json);
        Assert.Equal(1, insufficient.RootElement.GetProperty("secuencia").GetInt64());
        Assert.Equal(2, correct.RootElement.GetProperty("secuencia").GetInt64());
        Assert.Equal(5m, insufficient.RootElement.GetProperty("monto").GetDecimal());
        Assert.Equal(7m, correct.RootElement.GetProperty("monto").GetDecimal());
        Assert.Equal(12m, correct.RootElement.GetProperty("montoAcumulado").GetDecimal());
    }

    [Fact]
    public async Task PendingMoneyHasNoTimeLimitAndSurvivesNewDatabaseContexts()
    {
        await using var host = new PaymentHost();
        await host.Ingest(5m, at: DateTimeOffset.UtcNow.AddYears(-3));
        var second = await host.Ingest(7m);
        Assert.True(second.Validacion!.Correcto);
        Assert.Equal(12m, second.Validacion.MontoAcumulado);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
        var balance = Assert.Single(await db.BbvaPaymentBalances.AsNoTracking().ToListAsync());
        Assert.Equal(0m, balance.PendingAmount);
        Assert.Equal(2, balance.LastSequence);
    }

    [Fact]
    public async Task AccumulationSurvivesClosingAndReopeningTheDatabase()
    {
        var database = Path.Combine(Path.GetTempPath(), "listener-balance-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var first = new PaymentHost(databaseFile: database))
                Assert.False((await first.Ingest(5m)).Validacion!.Correcto);
            await using (var restarted = new PaymentHost(databaseFile: database))
            {
                var completed = await restarted.Ingest(7m);
                Assert.True(completed.Validacion!.Correcto);
                Assert.Equal(12m, completed.Validacion.MontoAcumulado);
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(database + suffix)) File.Delete(database + suffix);
        }
    }

    [Fact]
    public async Task NamesAreNormalizedButDifferentPeopleAreNeverCombined()
    {
        await using var host = new PaymentHost();
        var first = await host.Ingest(5m, name: "Juan  Perez");
        var other = await host.Ingest(7m, name: "ANA PEREZ");
        Assert.False(other.Validacion!.Correcto);
        var same = await host.Ingest(5m, name: "JUAN PEREZ");
        Assert.True(same.Validacion!.Correcto);
        Assert.Equal(10m, same.Validacion.MontoAcumulado);
        Assert.Equal(first.Validacion!.AcumulacionId, same.Validacion.AcumulacionId);
        Assert.NotEqual(other.Validacion.AcumulacionId, same.Validacion.AcumulacionId);
    }

    [Fact]
    public async Task ACompletedTotalIsConsumedAndTheNextPaymentStartsAnotherGroup()
    {
        await using var host = new PaymentHost();
        var first = await host.Ingest(5m, "one");
        var completed = await host.Ingest(7m, "two");
        var next = await host.Ingest(4m, "three");
        Assert.False(next.Validacion!.Correcto);
        Assert.Equal(4m, next.Validacion.MontoAcumulado);
        Assert.NotEqual(completed.Validacion!.AcumulacionId, next.Validacion.AcumulacionId);
        var duplicate = await host.Ingest(99m, "two");
        Assert.True(duplicate.Duplicada);
        Assert.Equal(completed.Id, duplicate.Id);
        Assert.Equal(12m, duplicate.Validacion!.MontoAcumulado);
        var last = await host.Ingest(6m, "four");
        Assert.True(last.Validacion!.Correcto);
        Assert.Equal(10m, last.Validacion.MontoAcumulado);
        Assert.Equal(next.Validacion.AcumulacionId, last.Validacion.AcumulacionId);
        Assert.Equal(4, last.Validacion.Secuencia);
        Assert.Equal(5m, first.Validacion!.MontoAcumulado);
    }

    [Fact]
    public async Task ASecondPaymentOfTenOrMoreAlsoCompletesTheExistingPendingTotal()
    {
        await using var host = new PaymentHost();
        await host.Ingest(5m);
        var next = await host.Ingest(20m);
        Assert.True(next.Validacion!.Correcto);
        Assert.Equal(25m, next.Validacion.MontoAcumulado);
        Assert.Equal(20m, next.Validacion.MontoRecibido);
    }

    [Fact]
    public async Task OldUnsentPaymentsAreValidatedOnceButAlreadyDeliveredMoneyIsNotReused()
    {
        await using var host = new PaymentHost();
        await AddLegacy(host, 5m, delivered: true);
        await AddLegacy(host, 5m);
        await AddLegacy(host, 7m);
        Assert.Equal(1, await host.Dispatch());
        Assert.Equal(1, await host.Dispatch());
        Assert.Equal(0, await host.Dispatch());
        Assert.Equal(2, host.Handler.Calls.Count);
        using var correct = JsonDocument.Parse(host.Handler.Calls[1].Json);
        Assert.Equal(12m, correct.RootElement.GetProperty("montoAcumulado").GetDecimal());
    }

    [Fact]
    public async Task MissingEndpointDoesNotLoseTheAccumulationOrCreateExtraRequests()
    {
        await using var host = new PaymentHost(abonosUrl: "");
        Assert.Equal("PENDIENTE_CONFIGURACION", (await host.Ingest(5m)).EstadoEnvio);
        var completed = await host.Ingest(7m);
        Assert.True(completed.Validacion!.Correcto);
        Assert.Equal(0, await host.Dispatch());
        Assert.Empty(host.Handler.Calls);
        host.Configuration["TonysGo:AbonosUrl"] = PaymentHost.AbonosUrl;
        Assert.Equal(1, await host.Dispatch());
        Assert.Equal(1, await host.Dispatch());
    }

    [Fact]
    public async Task RetriesKeepTheSameResultAndCannotDeliverCorrectBeforeAnEarlierInsufficientResult()
    {
        await using var host = new PaymentHost();
        var first = await host.Ingest(5m, "first");
        var second = await host.Ingest(7m, "second");
        host.Handler.Status = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(0, await host.Dispatch());
        Assert.Equal(0, await host.Dispatch());
        Assert.Single(host.Handler.Calls);
        host.Handler.Status = HttpStatusCode.OK;
        await host.Ingest(10m, name: "OTRA PERSONA");
        Assert.Equal(1, await host.Dispatch()); // another name can progress while the first is retrying
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
            var failed = Assert.Single(await db.EventosAuditoria.Where(x => x.TipoEvento == BbvaAbonoDispatcher.FailedEvent).ToListAsync());
            failed.Fecha = DateTimeOffset.UtcNow.AddMinutes(-2);
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, await host.Dispatch());
        Assert.Equal(1, await host.Dispatch());
        Assert.Equal(4, host.Handler.Calls.Count);
        Assert.Equal(host.Handler.Calls[0].Json, host.Handler.Calls[2].Json);
        var duplicate = await host.Ingest(99m, "second");
        Assert.Equal(second.Id, duplicate.Id);
        Assert.Equal(12m, duplicate.Validacion!.MontoAcumulado);
        Assert.Equal("ENVIADO_A_TONYSGO", duplicate.EstadoEnvio);
        var androidReceipt = JsonSerializer.Deserialize<BbvaIngestResult>(JsonSerializer.Serialize(duplicate, BbvaNotificationService.JsonOptions), BbvaNotificationService.JsonOptions)!;
        Assert.True(androidReceipt.EsAbono);
        Assert.Equal(7m, androidReceipt.Monto);
        Assert.Equal(first.Validacion!.AcumulacionId, duplicate.Validacion.AcumulacionId);
    }

    [Fact]
    public async Task SenderRejectsContradictoryValidationAndOldSubminimumAbonos()
    {
        await using var host = new PaymentHost();
        var sender = host.Services.GetRequiredService<BbvaAbonoSender>();
        Assert.Equal("MONTO_INSUFICIENTE", await sender.SendAsync(new(Guid.NewGuid(), "PRUEBA", 5m, "PEN", DateTimeOffset.UtcNow), default));
        var invalid = new BbvaValidationResult(Guid.NewGuid(), Guid.NewGuid(), 1, "PRUEBA", true, 5m, 5m, 10m, 0, "OK", "Prueba", DateTimeOffset.UtcNow);
        Assert.Equal("VALIDACION_GUARDADA_INVALIDA", await sender.SendValidationAsync(invalid, default));
        Assert.Empty(host.Handler.Calls);
    }

    private static async Task AddLegacy(PaymentHost host, decimal amount, bool delivered = false)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
        var notice = BbvaNotificationTests.Notice($"PERSONA PRUEBA te hizo un pago de S/ {amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
        var row = new CorreoProcesado
        {
            EstadoSincronizacionCorreo = new() { Buzon = Guid.NewGuid().ToString("N"), Carpeta = "NOTIFICATIONS", UidValidity = 1 },
            Uid = 1, UidValidity = 1, MessageId = Guid.NewGuid().ToString("N"), FechaRecepcion = notice.PostedAt,
            HashContenido = new string('A', 64), EstadoProcesamiento = EstadoProcesamientoCorreo.Procesado,
            CodigoMotivo = "ABONO_BBVA_EXTRAIDO", VersionExtractor = BbvaNotificationService.Extractor,
            ResultadoJson = JsonSerializer.Serialize(new StoredBbvaNotification(notice, BbvaNotificationParser.Parse(notice)), BbvaNotificationService.JsonOptions)
        };
        db.CorreosProcesados.Add(row);
        if (delivered) db.EventosAuditoria.Add(new() { CorreoProcesadoId = row.Id, TipoEvento = BbvaAbonoDispatcher.DeliveredEvent,
            Actor = "OldWorker", CodigoMotivo = "HTTP_2XX", IdCorrelacion = row.Id.ToString() });
        await db.SaveChangesAsync();
    }

    private sealed class PaymentHost : IAsyncDisposable
    {
        public const string AbonosUrl = "https://tonysgo.example/abonos";
        private readonly SqliteConnection _connection;
        public readonly RecordingHandler Handler = new();
        public ServiceProvider Services { get; private set; }
        public IConfigurationRoot Configuration { get; private set; } = null!;
        public PaymentHost(string abonosUrl = AbonosUrl, string? databaseFile = null)
        {
            _connection = new(new SqliteConnectionStringBuilder { DataSource = databaseFile ?? ":memory:", Pooling = false }.ToString());
            _connection.Open();
            var services = new ServiceCollection();
            services.AddLogging();
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TonysGo:AbonosUrl"] = abonosUrl,
                ["TonysGo:CallbackApiKey"] = new string('k', 32)
            }).Build();
            services.AddSingleton<IConfiguration>(Configuration);
            services.AddSingleton<IHostEnvironment>(new TestEnvironment());
            services.AddSingleton<IHttpClientFactory>(new RecordingClients(Handler));
            services.AddDbContext<ListenerDbContext>(options => options.UseSqlite(_connection).ReplaceService<IModelCustomizer, HistoryTestModel>());
            services.AddScoped<BbvaNotificationService>(); services.AddScoped<NotificationHistory>();
            services.AddSingleton<BbvaAbonoSender>(); services.AddSingleton<BbvaAbonoDispatcher>();
            Services = services.BuildServiceProvider();
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ListenerDbContext>().Database.EnsureCreated();
        }
        public async Task<BbvaNotificationReceipt> Ingest(decimal amount, string? key = null, string name = "PERSONA PRUEBA", DateTimeOffset? at = null)
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<BbvaNotificationService>().IngestAsync(
                BbvaNotificationTests.Notice($"{name} te hizo un pago de S/ {amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}")
                    with { NotificationKey = key ?? Guid.NewGuid().ToString("N"), PostedAt = at ?? DateTimeOffset.UtcNow }, default);
        }
        public Task<int> Dispatch() => Services.GetRequiredService<BbvaAbonoDispatcher>().DispatchPendingAsync(default);
        public async ValueTask DisposeAsync() { await Services.DisposeAsync(); await _connection.DisposeAsync(); }
    }
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Listener.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class RecordingClients(RecordingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public List<(string Url, string Json, string Key)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.RequestUri!.AbsoluteUri, await request.Content!.ReadAsStringAsync(ct), request.Headers.GetValues("X-Listener-Api-Key").Single()));
            return new(Status);
        }
    }
}
