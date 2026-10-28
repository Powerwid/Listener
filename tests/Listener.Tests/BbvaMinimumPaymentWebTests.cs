using System.Net;
using System.Net.Http.Json;
using Listener.Api.Bbva;
using Listener.Api.Data;
using Listener.Api.Devices;
using Listener.Api.History;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TonysGo.Listener.Bbva;
using TonysGo.Listener.Pairing;

namespace Listener.Tests;

public sealed class BbvaMinimumPaymentWebTests
{
    [Fact]
    public async Task ExistingAndroidInputGetsASuccessfulReceiptAndTheWebSeparatesInsufficientPayments()
    {
        await using var factory = new HistoryWebFactory();
        using var admin = factory.CreateClient(new() { AllowAutoRedirect = false });
        await NotificationHistoryWebTests.Login(admin);
        DevicePairingQr invite;
        using (var scope = factory.Services.CreateScope())
            invite = await scope.ServiceProvider.GetRequiredService<DevicePairingService>().CreateAsync("https://listener.example", "admin", default);
        using var phone = factory.CreateClient(new() { HandleCookies = false });
        var pairing = await phone.PostAsJsonAsync("/api/v1/devices/pair", new DevicePairingRequest(
            invite.Token, Guid.NewGuid().ToString("N"), "minimum-phone", "Android de prueba"));
        var credential = (await pairing.Content.ReadFromJsonAsync<DevicePairingResponse>())!.Credential;
        phone.DefaultRequestHeaders.Add("X-Listener-Api-Key", credential);
        var small = BbvaNotificationTests.Notice("PERSONA MINIMO te hizo un pago de S/ 5.00")
            with { NotificationKey = "minimum-five", DeviceId = "minimum-phone" };
        using var response = await phone.PostAsJsonAsync("/api/v1/bbva/notifications", small);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<BbvaNotificationReceipt>())!;
        Assert.True(receipt.EsAbono);
        Assert.False(receipt.Validacion!.Correcto);
        Assert.Equal(5m, receipt.Validacion.MontoFaltante);
        Assert.Equal("PENDIENTE_ENVIO", receipt.EstadoEnvio);
        Assert.Contains("Recibimos S/ 5.00", receipt.Validacion.Mensaje);

        using var retry = await phone.PostAsJsonAsync("/api/v1/bbva/notifications", small);
        var duplicate = (await retry.Content.ReadFromJsonAsync<BbvaIngestResult>())!;
        Assert.True(duplicate.Duplicada);
        Assert.Equal(receipt.Id, duplicate.Id);
        Assert.True(duplicate.EsAbono);
        using var normal = await phone.PostAsJsonAsync("/api/v1/bbva/notifications", small with
        {
            NotificationKey = "minimum-ten", Text = "PERSONA MINIMO te hizo un pago de S/ 10.00"
        });
        Assert.True((await normal.Content.ReadFromJsonAsync<BbvaNotificationReceipt>())!.Validacion!.Correcto);

        var history = (await admin.GetFromJsonAsync<HistoryPage>("/api/v1/admin/history?status=insufficient"))!;
        var item = Assert.Single(history.Items);
        Assert.Equal(receipt.Id, item.Id);
        Assert.Equal(1, history.Summary.Insufficient);
        Assert.Equal("Monto insuficiente", item.Classification);
        Assert.Equal("Pendiente de entrega", item.StatusLabel);
        Assert.Equal(5m, item.Amount);
        Assert.Equal(5m, item.Validation!.MontoFaltante);
        var detail = (await admin.GetFromJsonAsync<HistoryDetail>($"/api/v1/admin/history/{receipt.Id}"))!;
        Assert.Equal(small.Text, detail.Text);
        using var read = factory.Services.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<ListenerDbContext>();
        Assert.Equal(2, await db.CorreosProcesados.CountAsync(x => x.EstadoSincronizacionCorreo.Buzon == "android-bbva:minimum-phone"));
        Assert.Empty(await db.EvidenciasPago.ToListAsync());
        Assert.Empty(await db.SolicitudesVerificacion.ToListAsync());
    }

    [Fact]
    public async Task ValidationDeliveryHasTheUsualFiltersAndDoesNotApproveARecharge()
    {
        await using var factory = new HistoryWebFactory();
        using var admin = factory.CreateClient(new() { AllowAutoRedirect = false });
        await NotificationHistoryWebTests.Login(admin);
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var input = BbvaNotificationTests.Notice("PERSONA AVISO te hizo un pago de S/ 9.99") with { DeviceId = "notice-history-phone" };
            var receipt = await scope.ServiceProvider.GetRequiredService<BbvaNotificationService>().IngestAsync(input, default);
            id = receipt.Id;
            var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
            db.EventosAuditoria.Add(new() { CorreoProcesadoId = id, TipoEvento = BbvaAbonoDispatcher.ValidationDeliveredEvent,
                Actor = "BbvaAbonoWorker", CodigoMotivo = "HTTP_2XX", IdCorrelacion = id.ToString() });
            await db.SaveChangesAsync();
        }
        var notices = (await admin.GetFromJsonAsync<HistoryPage>("/api/v1/admin/history?status=insufficient"))!;
        var item = Assert.Single(notices.Items);
        Assert.Equal("sent", item.Status);
        Assert.Equal("Respuesta entregada a Tony’s Go", item.StatusLabel);
        Assert.Equal(0.01m, item.Validation!.MontoFaltante);
        Assert.Equal(0, notices.Summary.Pending);
        Assert.Equal(1, notices.Summary.Sent);
        var delivered = (await admin.GetFromJsonAsync<HistoryPage>("/api/v1/admin/history?status=sent"))!;
        Assert.Contains(delivered.Items, row => row.Id == id);
        var pending = (await admin.GetFromJsonAsync<HistoryPage>("/api/v1/admin/history?status=pending"))!;
        Assert.DoesNotContain(pending.Items, row => row.Id == id);
    }
}
