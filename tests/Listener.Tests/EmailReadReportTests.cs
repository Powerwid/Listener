using System.Text.Json;
using Listener.Api.Email;
using MimeKit;

namespace Listener.Tests;

public class EmailReadReportTests
{
    [Fact]
    public async Task BatchFiltersRealMimeInputsAndKeepsAnErrorWithoutLosingOtherResults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "listener-mail-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var paths = new List<string>();
        try
        {
            var samples = new[]
            {
                ("yape.eml", BcpEmailParser.SenderAddress,
                    "Constancia de recepción de Yapeo a celular BCP - Servicio de Notificaciones BCP",
                    "Recibiste un yapeo de S/ 12.00 de Persona Ejemplo. Monto recibido S/ 12.00 Operación realizada Yapeo a celular Fecha y hora 04 de septiembre de 2026 - 12:05 AM Enviado por Persona Ejemplo ¿No reconoces esta operación?"),
                ("consumo.eml", BcpEmailParser.SenderAddress,
                    "Realizaste un consumo con tu Tarjeta de Débito BCP",
                    "Total del consumo S/ 85.02 Operación realizada Consumo Tarjeta de Débito Fecha y hora 14 de setiembre de 2026 - 12:56 PM Empresa TIENDA EJEMPLO Número de operación 000123 ¿No reconoces esta operación?"),
                ("transferencia.eml", BcpEmailParser.SenderAddress,
                    "Aviso de transferencia recibido - fixture sintetico, no plantilla bancaria",
                    "Este texto deliberadamente no define una plantilla real ni un importe."),
                ("remitente-falso.eml", "notificaciones@notificacionesbcp.com.pe.example.invalid",
                    "Constancia de recepción de Yapeo a celular BCP", "Recibiste un yapeo de S/ 12.00")
            };
            foreach (var sample in samples)
            {
                using var mail = new MimeMessage();
                mail.From.Add(new MailboxAddress("BCP Notificaciones", sample.Item2));
                mail.Subject = sample.Item3;
                mail.Body = new TextPart("plain") { Text = sample.Item4 };
                var path = Path.Combine(directory, sample.Item1);
                paths.Add(path);
                await mail.WriteToAsync(path);
            }
            paths.Add(Path.Combine(directory, "inexistente.eml"));
            var report = await BcpEmailCommand.InspectFilesAsync(paths, CancellationToken.None);

            Assert.Single(report.IngresosExtraidos);
            Assert.Equal(12m, report.IngresosExtraidos[0].Datos!.Monto);
            Assert.Equal(2, report.Descartados.Count);
            Assert.Single(report.Revision);
            Assert.Equal("PLANTILLA_TRANSFERENCIA_PENDIENTE", report.Revision[0].Datos!.Motivo);
            Assert.Null(report.Revision[0].Datos!.Monto);
            Assert.Equal("Desconocida", report.Revision[0].Datos!.Direccion);
            Assert.Single(report.Errores);
            Assert.Equal("ARCHIVO_NO_DISPONIBLE", report.Errores[0].Error);
            Assert.All(report.IngresosExtraidos.Concat(report.Descartados).Concat(report.Revision),
                item => Assert.False(item.Datos!.PagoVerificado));

            using var json = JsonDocument.Parse(JsonSerializer.Serialize(report, BcpEmailCommand.JsonOptions));
            Assert.Equal(5, json.RootElement.GetProperty("resumen").GetProperty("procesados").GetInt32());
            Assert.DoesNotContain(directory, JsonSerializer.Serialize(report));
        }
        finally
        {
            // Delete only the exact fixture paths created by this test, never recursively.
            foreach (var path in paths) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void EmptyMailboxProducesEmptyCollectionsAndZeroSummary()
    {
        var report = EmailReadReport.Create([]);
        Assert.Empty(report.IngresosExtraidos);
        Assert.Empty(report.Descartados);
        Assert.Empty(report.Revision);
        Assert.Empty(report.Errores);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report, BcpEmailCommand.JsonOptions));
        Assert.Equal(0, json.RootElement.GetProperty("resumen").GetProperty("procesados").GetInt32());
    }

    [Fact]
    public void IncompleteIncomingEvidenceGoesToReviewInsteadOfSuccessfulExtraction()
    {
        var result = new BcpEmailResult { TipoOperacion = "YapeoRecibido", Direccion = "Ingreso", Motivo = "DATOS_INCOMPLETOS_O_INCONSISTENTES" };
        var report = EmailReadReport.Create([new("1", result)]);
        Assert.Empty(report.IngresosExtraidos);
        Assert.Single(report.Revision);
    }

    [Fact]
    public async Task CancelledBatchReportsEachFileWithoutReading()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var report = await BcpEmailCommand.InspectFilesAsync(["a.eml", "b.eml"], cancellation.Token);
        Assert.Equal(2, report.Errores.Count);
        Assert.All(report.Errores, item => Assert.Equal("LECTURA_CANCELADA", item.Error));
    }
}
