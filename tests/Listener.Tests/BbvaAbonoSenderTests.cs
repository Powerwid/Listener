using System.Net;
using System.Text.Json;
using Listener.Api.Bbva;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Listener.Tests;

public class BbvaAbonoSenderTests
{
    [Fact]
    public async Task SendsNameAndAmountWithAStableEventIdWithoutApprovingARecharge()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK);
        var sender = Sender(handler);
        var abono = new BbvaAbono(Guid.NewGuid(), "VICTOR RAUL MAMANI QUEA", 20m, "PEN", DateTimeOffset.UtcNow);
        Assert.Equal("HTTP_2XX", await sender.SendAsync(abono, default));
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal(abono.EventoId, json.RootElement.GetProperty("eventoId").GetGuid());
        Assert.Equal(abono.NombreCompleto, json.RootElement.GetProperty("nombreCompleto").GetString());
        Assert.Equal(20m, json.RootElement.GetProperty("monto").GetDecimal());
        Assert.False(json.RootElement.TryGetProperty("codigoSeguridad", out _));
        Assert.False(json.RootElement.TryGetProperty("referenciaConductor", out _));
        Assert.False(json.RootElement.TryGetProperty("verificationId", out _));
        Assert.Equal(new string('k', 32), handler.ApiKey);
        Assert.Equal("https://tonysgo.example/api/internal/bbva/abonos", handler.Url);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "HTTP_404")]
    [InlineData(HttpStatusCode.Unauthorized, "HTTP_401")]
    [InlineData(HttpStatusCode.TooManyRequests, "HTTP_429")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "HTTP_503")]
    public async Task FailedDeliveriesRemainUnconfirmed(HttpStatusCode status, string expected)
    {
        Assert.Equal(expected, await Sender(new CaptureHandler(status)).SendAsync(
            new BbvaAbono(Guid.NewGuid(), "PERSONA PRUEBA", 1m, "PEN", DateTimeOffset.UtcNow), default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://example.com/abonos")]
    public async Task MissingOrInsecureCallbackDoesNotSend(string url)
    {
        var handler = new CaptureHandler(HttpStatusCode.OK);
        var sender = Sender(handler, url);
        Assert.False(sender.IsConfigured);
        Assert.Equal("ENVIO_NO_CONFIGURADO", await sender.SendAsync(new BbvaAbono(
            Guid.NewGuid(), "PERSONA PRUEBA", 1m, "PEN", DateTimeOffset.UtcNow), default));
        Assert.Null(handler.Body);
    }

    private static BbvaAbonoSender Sender(CaptureHandler handler, string url = "https://tonysgo.example/api/internal/bbva/abonos") =>
        new(new ClientFactory(handler), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TonysGo:AbonosUrl"] = url, ["TonysGo:CallbackApiKey"] = new string('k', 32)
        }).Build(), new EnvironmentStub());

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class CaptureHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Body, ApiKey, Url;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            ApiKey = request.Headers.GetValues("X-Listener-Api-Key").Single();
            Url = request.RequestUri!.ToString();
            return new HttpResponseMessage(status);
        }
    }
    private sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Listener.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
