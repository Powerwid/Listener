using System.Text;
using Listener.Api.Email;
using MimeKit;

namespace Listener.Tests;

public class BcpEmailParserTests
{
    private static MimeMessage Yape(string amount = "12.00", string date = "04 de septiembre de 2026 - 12:05 AM") => new()
    {
        From = { new MailboxAddress("BCP Notificaciones", BcpEmailParser.SenderAddress) },
        Subject = "Constancia de recepción de Yapeo a celular BCP - Servicio de Notificaciones BCP",
        Body = new TextPart("html")
        {
            Text = $"<html><head><style>.unused {{ color: red; }}</style></head><body><p>Recibiste un yapeo de S/ 12.00 de Persona Ejemplo.</p>"
                + $"<table><tr><td>Monto recibido</td><td>S/&nbsp;{amount}</td></tr><tr><td>Operación realizada</td><td>Yapeo a celular</td></tr>"
                + $"<tr><td>Fecha y hora</td><td>{date}</td></tr><tr><td>Enviado por</td><td>Persona Ejemplo</td></tr></table>"
                + "<p>¿No reconoces esta operación?</p></body></html>"
        }
    };

    [Fact]
    public void IncomingYapeExtractsFieldsWithoutApproving()
    {
        using var mail = Yape();
        var result = BcpEmailParser.Parse(mail);
        Assert.Equal("YapeoRecibido", result.TipoOperacion);
        Assert.Equal("Ingreso", result.Direccion);
        Assert.Equal(12m, result.Monto);
        Assert.Equal("PEN", result.Moneda);
        Assert.Equal("Persona Ejemplo", result.Ordenante);
        Assert.Equal(new DateTimeOffset(2026, 9, 4, 0, 5, 0, TimeSpan.FromHours(-5)), result.FechaOperacion);
        Assert.Null(result.NumeroOperacion);
        Assert.Null(result.ReferenciaCuentaReceptora);
        Assert.Equal("INGRESO_EXTRAIDO_PENDIENTE_AUTENTICIDAD_Y_VINCULACION", result.Motivo);
        Assert.False(result.PagoVerificado);
    }

    [Fact]
    public void DebitIsExcludedDespiteOperationNumberAndClaimedAuthentication()
    {
        using var mail = Yape();
        mail.Subject = "Realizaste un consumo con tu Tarjeta de Débito BCP - Servicio de Notificaciones BCP";
        mail.Headers.Add("Authentication-Results", "mx.google.com; dkim=pass; spf=pass; dmarc=pass");
        mail.Body = new TextPart("plain") { Text = "Total del consumo S/ 85.02 Datos de la operación Operación realizada Consumo Tarjeta de Débito Fecha y hora 14 de setiembre de 2026 - 12:56 PM Número de Tarjeta de Débito ************0000 Empresa COMERCIO EJEMPLO Número de operación 000328 ¿No reconoces esta operación?" };
        var result = BcpEmailParser.Parse(mail);
        Assert.Equal("Descartado", result.Estado);
        Assert.Equal("Egreso", result.Direccion);
        Assert.Equal("OPERACION_NO_CORRESPONDE_A_RECARGA", result.Motivo);
        Assert.Equal(85.02m, result.Monto);
        Assert.Equal("000328", result.NumeroOperacion);
        Assert.Equal("COMERCIO EJEMPLO", result.Empresa);
        Assert.Equal(12, result.FechaOperacion?.Hour);
        Assert.Equal("NoVerificada", result.AutenticidadCorreo);
        Assert.False(result.PagoVerificado);
    }

    [Theory]
    [InlineData("13.00")]
    [InlineData("0.00")]
    [InlineData("-12.00")]
    [InlineData("12,00")]
    [InlineData("12.001")]
    public void InvalidOrConflictingAmountsRequireReview(string amount)
    {
        using var mail = Yape(amount);
        var result = BcpEmailParser.Parse(mail);
        Assert.Equal("DATOS_INCOMPLETOS_O_INCONSISTENTES", result.Motivo);
        Assert.Equal("RequiereRevision", result.Estado);
        Assert.False(result.PagoVerificado);
    }

    [Theory]
    [InlineData("31 de febrero de 2026 - 12:05 AM")]
    [InlineData("04 de septiembre de 2026 - 00:05 AM")]
    [InlineData("04 de septiembre de 2026 - 13:05 PM")]
    [InlineData("04 de septiembre de 2026 - 12:60 AM")]
    public void InvalidDatesAreNotGuessed(string date)
    {
        using var mail = Yape(date: date);
        Assert.Null(BcpEmailParser.Parse(mail).FechaOperacion);
    }

    [Fact]
    public void DisplayNameDoesNotAllowAnotherSender()
    {
        using var mail = Yape();
        mail.From.Clear();
        mail.From.Add(new MailboxAddress(BcpEmailParser.SenderAddress, "fake@example.invalid"));
        Assert.Equal("REMITENTE_NO_PERMITIDO", BcpEmailParser.Parse(mail).Motivo);
    }

    [Fact]
    public void MultipleSendersAreRejected()
    {
        using var mail = Yape();
        mail.From.Add(new MailboxAddress("Extra", "extra@example.invalid"));
        Assert.Equal("REMITENTE_NO_PERMITIDO", BcpEmailParser.Parse(mail).Motivo);
    }

    [Fact]
    public void ForwardedSubjectIsNotRecognizedAsOriginal()
    {
        using var mail = Yape();
        mail.Subject = "Fwd: " + mail.Subject;
        Assert.Equal("FORMATO_NO_RECONOCIDO", BcpEmailParser.Parse(mail).Motivo);
    }

    [Fact]
    public void DuplicateAmountFieldsRequireReview()
    {
        using var mail = Yape();
        Assert.IsType<TextPart>(mail.Body).Text += "<p>Monto recibido S/ 12.00</p>";
        Assert.Null(BcpEmailParser.Parse(mail).Monto);
    }

    [Fact]
    public void ServicePaymentIsExcluded()
    {
        using var mail = Yape();
        mail.Subject = "Pago de servicios";
        mail.Body = new TextPart("plain") { Text = "Operación realizada: Pago de servicios Empresa SENATI" };
        Assert.Equal("Descartado", BcpEmailParser.Parse(mail).Estado);
    }

    [Fact]
    public async Task MimeDecodesTransferEncodingAndHtmlEntities()
    {
        using var mail = Yape();
        Assert.IsType<TextPart>(mail.Body).ContentTransferEncoding = ContentEncoding.Base64;
        using var stream = new MemoryStream();
        await mail.WriteToAsync(stream);
        stream.Position = 0;
        Assert.Equal(12m, (await BcpEmailCommand.InspectAsync(stream, CancellationToken.None)).Monto);
    }

    [Fact]
    public async Task OversizedFileIsRejected()
    {
        using var stream = new MemoryStream(new byte[BcpEmailCommand.MaximumMessageBytes + 1]);
        await Assert.ThrowsAsync<FormatException>(() => BcpEmailCommand.InspectAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task CancelledReadStopsBeforeNetwork()
    {
        using var timeout = new CancellationTokenSource();
        timeout.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GmailBcpReader.ReadOnceAsync("sample@gmail.com", "abcdefghijklmnop", timeout.Token));
    }
}
