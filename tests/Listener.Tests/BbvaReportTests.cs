using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using Listener.Api.Bbva;
using Listener.Api.Data;
using Listener.Api.Data.Entities;
using Listener.Api.History;
using Listener.Api.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Listener.Tests;

public sealed class BbvaReportTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task ReportAndExcelRequireTheAdministratorAndDoNotCachePrivateData(string environment)
    {
        await using var factory = new HistoryWebFactory(environment);
        using var browser = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/reportes")).StatusCode);
        foreach (var path in new[] { "/api/v1/admin/reports", "/api/v1/admin/reports/excel" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(path)).StatusCode);
        await NotificationHistoryWebTests.Login(browser);
        using var page = await browser.GetAsync("/reportes");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl!.NoStore);
        Assert.True(page.Headers.Contains("Content-Security-Policy"));
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Exportar Excel", html);
        Assert.DoesNotContain("id=\"source\"", html);
        var history = await browser.GetStringAsync("/historial");
        Assert.Contains("href=\"/reportes\"", history);
        Assert.DoesNotContain("id=\"source\"", history);
        Assert.DoesNotContain("Origen / teléfono", history);
        Assert.Contains("href=\"/reportes\"", await browser.GetStringAsync("/dispositivos"));
    }

    [Fact]
    public async Task ExcelContainsAllFilteredBbvaRowsEvenWhenThePreviewIsOnAnotherPage()
    {
        await using var factory = new HistoryWebFactory();
        using var browser = factory.CreateClient(new() { AllowAutoRedirect = false });
        await NotificationHistoryWebTests.Login(browser);
        var preview = (await browser.GetFromJsonAsync<HistoryPage>("/api/v1/admin/reports?page=2&source=yape"))!;
        Assert.Equal(27, preview.Summary.Total);
        Assert.Equal(2, preview.Items.Count);
        Assert.All(preview.Items, item => Assert.Equal("BBVA Empresas", item.Source));
        using var response = await browser.GetAsync("/api/v1/admin/reports/excel?page=2&source=correo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BbvaReportWorkbook.ContentType, response.Content.Headers.ContentType!.MediaType);
        Assert.Contains(".xlsx", response.Content.Headers.ContentDisposition!.ToString());
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var workbook = SpreadsheetDocument.Open(stream, false);
        Valid(workbook);
        var sheet = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet!;
        Assert.Equal(27, DataRows(sheet).Count());
        Assert.Equal("337.50", Cell(sheet, "D3").CellValue!.Text);
        Assert.DoesNotContain("CORREO ANTERIOR", sheet.InnerText);
        Assert.DoesNotContain("José MUÑOZ PÉREZ", sheet.InnerText);
        Assert.Equal("A7", sheet.Descendants<Pane>().Single().TopLeftCell!.Value);
        Assert.Equal("A6:G33", sheet.Elements<AutoFilter>().Single().Reference!.Value);
    }

    [Fact]
    public async Task DateAndValidationFiltersUsePeruTimeAndExportTheSameRowsWithoutChangingPayments()
    {
        await using var factory = new HistoryWebFactory();
        using var browser = factory.CreateClient(new() { AllowAutoRedirect = false });
        await NotificationHistoryWebTests.Login(browser);
        Guid completedId;
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BbvaNotificationService>();
            await service.IngestAsync(Notice("PRUEBA REPORTE", "5.00", "2026-10-20T04:59:59Z"), default);
            var completed = await service.IngestAsync(Notice("PRUEBA REPORTE", "7.00", "2026-10-20T05:00:00Z"), default);
            completedId = completed.Id;
            await service.IngestAsync(Notice("OTRO REPORTE", "9.99", "2026-10-21T04:59:59Z"), default);
            await service.IngestAsync(Notice("FUERA DE FECHA", "20.00", "2026-10-21T05:00:00Z"), default);
            var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
            db.EventosAuditoria.Add(new() { CorreoProcesadoId = completed.Id, TipoEvento = BbvaAbonoDispatcher.ValidationDeliveredEvent,
                Actor = "report-test", CodigoMotivo = "HTTP_2XX", IdCorrelacion = completed.Id.ToString() });
            await db.SaveChangesAsync();
        }
        const string filters = "from=2026-10-20&to=2026-10-20&status=correct";
        var preview = (await browser.GetFromJsonAsync<HistoryPage>("/api/v1/admin/reports?" + filters))!;
        var item = Assert.Single(preview.Items);
        Assert.Equal(completedId, item.Id);
        Assert.True(item.Validation!.Correcto);
        Assert.Equal("sent", item.Status);
        var insufficient = (await browser.GetFromJsonAsync<HistoryPage>("/api/v1/admin/reports?from=2026-10-20&to=2026-10-20&status=insufficient"))!;
        Assert.Equal("OTRO REPORTE", Assert.Single(insufficient.Items).Name);
        using var response = await browser.GetAsync("/api/v1/admin/reports/excel?" + filters);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var workbook = SpreadsheetDocument.Open(stream, false);
        Valid(workbook);
        var sheet = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet!;
        Assert.Single(DataRows(sheet));
        Assert.Equal("PRUEBA REPORTE", Cell(sheet, "B7").InnerText);
        Assert.Equal(7m, decimal.Parse(Cell(sheet, "C7").CellValue!.Text, CultureInfo.InvariantCulture));
        Assert.Equal(12m, decimal.Parse(Cell(sheet, "D7").CellValue!.Text, CultureInfo.InvariantCulture));
        Assert.Equal(0m, decimal.Parse(Cell(sheet, "E7").CellValue!.Text, CultureInfo.InvariantCulture));
        Assert.Equal(7m, decimal.Parse(Cell(sheet, "D3").CellValue!.Text, CultureInfo.InvariantCulture));
        Assert.Equal(new DateTime(2026, 10, 20), DateTime.FromOADate(double.Parse(Cell(sheet, "A7").CellValue!.Text, CultureInfo.InvariantCulture)));
        using var read = factory.Services.CreateScope();
        var stored = read.ServiceProvider.GetRequiredService<ListenerDbContext>();
        Assert.Equal(4, await stored.BbvaPaymentValidations.CountAsync());
        Assert.Equal(9.99m, await stored.BbvaPaymentBalances.SumAsync(x => x.PendingAmount));
        Assert.Empty(await stored.EvidenciasPago.ToListAsync());
        Assert.Empty(await stored.SolicitudesVerificacion.ToListAsync());
    }

    [Theory]
    [InlineData("from=2026-10-21&to=2026-10-20")]
    [InlineData("status=unexpected")]
    [InlineData("from=invalid")]
    public async Task InvalidFiltersAreRejectedForBothThePreviewAndTheExport(string filters)
    {
        await using var factory = new HistoryWebFactory();
        using var browser = factory.CreateClient(new() { AllowAutoRedirect = false });
        await NotificationHistoryWebTests.Login(browser);
        foreach (var path in new[] { "/api/v1/admin/reports?", "/api/v1/admin/reports/excel?" })
            Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync(path + filters)).StatusCode);
    }

    [Fact]
    public void WorkbookKeepsTextAsTextAndTotalsIndividualPaymentsInsteadOfAccumulatedSnapshots()
    {
        var at = new DateTimeOffset(2026, 10, 28, 5, 0, 0, TimeSpan.Zero);
        var name = "=HYPERLINK(\"https://example.com\",\"Texto\")";
        var first = Item(name, 5, 5, false, at);
        var second = Item("JOSÉ MUÑOZ PÉREZ", 7, 12, true, at);
        using var stream = new MemoryStream(BbvaReportWorkbook.Create([first, second], new(), at));
        using var workbook = SpreadsheetDocument.Open(stream, false);
        Valid(workbook);
        var sheet = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet!;
        Assert.Equal(CellValues.InlineString, Cell(sheet, "B7").DataType!.Value);
        Assert.Equal(name, Cell(sheet, "B7").InnerText);
        Assert.Equal("JOSÉ MUÑOZ PÉREZ", Cell(sheet, "B8").InnerText);
        Assert.Equal("12", Cell(sheet, "D3").CellValue!.Text);
        Assert.Equal(CellValues.Number, Cell(sheet, "C7").DataType!.Value);
        Assert.Empty(sheet.Descendants<CellFormula>());
        Assert.Empty(workbook.WorkbookPart.ExternalWorkbookParts);
    }

    [Fact]
    public void EmptyResultStillProducesAValidWorkbookWithHeadersAndZeroTotals()
    {
        using var stream = new MemoryStream(BbvaReportWorkbook.Create([], new(), DateTimeOffset.UtcNow));
        using var workbook = SpreadsheetDocument.Open(stream, false);
        Valid(workbook);
        var sheet = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet!;
        Assert.Empty(DataRows(sheet));
        Assert.Equal("0", Cell(sheet, "D3").CellValue!.Text);
        Assert.Equal("A6:G6", sheet.Elements<AutoFilter>().Single().Reference!.Value);
    }

    private static TonysGo.Listener.Bbva.BbvaNotificationRequest Notice(string name, string amount, string at) =>
        BbvaNotificationTests.Notice($"{name} te hizo un pago de S/ {amount}") with
        {
            NotificationKey = Guid.NewGuid().ToString("N"), DeviceId = "report-test-phone", PostedAt = DateTimeOffset.Parse(at, CultureInfo.InvariantCulture)
        };
    private static IEnumerable<Row> DataRows(Worksheet sheet) => sheet.Descendants<Row>().Where(x => x.RowIndex!.Value >= 7);
    private static Cell Cell(Worksheet sheet, string reference) => sheet.Descendants<Cell>().Single(x => x.CellReference!.Value == reference);
    private static void Valid(SpreadsheetDocument workbook)
    {
        var errors = new OpenXmlValidator().Validate(workbook).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors.Take(10).Select(x => x.Description + " " + x.Path?.XPath)));
    }
    private static HistoryItem Item(string name, decimal received, decimal accumulated, bool correct, DateTimeOffset at) =>
        new(Guid.NewGuid(), "BBVA Empresas", name, received, "PEN", "Cobro con QR", at, at, "report-test-phone",
            correct ? "Validación correcta" : "Monto insuficiente", "pending", "Pendiente de entrega", null,
            BbvaPaymentLedger.Result(new BbvaPaymentValidation { EventId = Guid.NewGuid(), PayerKey = "test", FullName = name,
                GroupId = Guid.NewGuid(), Sequence = 1, ReceivedAmount = received, AccumulatedAmount = accumulated, Correct = correct, NotificationAt = at }));
}
