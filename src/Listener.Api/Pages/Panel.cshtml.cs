using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text;
using Listener.Api.Diagnostics;
using Listener.Api.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Listener.Api.Automatic;

namespace Listener.Api.Pages;

[RequestSizeLimit(22 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 22 * 1024 * 1024)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PanelModel(AutomaticState automatic, NotificationQueries queries) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = GmailConnectionTest.DefaultEmail;
    [BindProperty, Range(1, 90)] public int Days { get; set; } = 30;
    [BindProperty, Range(1, 100)] public int Limit { get; set; } = 20;
    [BindProperty, StringLength(128)] public string? AppPassword { get; set; }
    [BindProperty] public List<IFormFile> Files { get; set; } = [];
    public EmailReadReport? Report { get; private set; }
    public string? Notice { get; private set; }
    public string? Failure { get; private set; }
    public AutomaticStatus AutomaticStatus => automatic.Status;
    public string ReportJson => JsonSerializer.Serialize(Report, BcpEmailCommand.JsonOptions);
    public IEnumerable<(string Group, EmailReadItem Item)> Rows => Report is null ? [] :
        Report.IngresosExtraidos.Select(i => ("Ingreso extraído", i))
            .Concat(Report.Descartados.Select(i => ("Descartado", i)))
            .Concat(Report.Revision.Select(i => ("Revisión", i)))
            .Concat(Report.Errores.Select(i => ("Error", i)));

    public async Task OnGetAsync()
    {
        if (automatic.Status.Buzon is { } email) Email = email;
        await LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var stored = await queries.LatestAsync(HttpContext.RequestAborted);
            Report = EmailReadReport.Create(stored.Select(x => new EmailReadItem(x.Id.ToString(), x.Datos, x.Error)).ToArray())
                with { Origen = "BaseDeDatos", Persistido = true, Limite = 100 };
        }
        catch (Exception) { Failure = "No se pudo consultar el historial. Revisa SQL Server y aplica las migraciones pendientes."; }
    }

    public async Task<IActionResult> OnPostStopAsync()
    {
        automatic.Stop();
        AppPassword = null;
        ModelState.Clear();
        Notice = "Lector detenido. Las credenciales se retiraron de la sesión; el historial permanece guardado.";
        await LoadHistoryAsync();
        return Page();
    }

    public IActionResult OnPostDownload(string? reportJson)
    {
        AppPassword = null;
        ModelState.Clear();
        try
        {
            if (string.IsNullOrEmpty(reportJson) || Encoding.UTF8.GetByteCount(reportJson) > 2 * 1024 * 1024)
                return BadRequest("Informe ausente o demasiado grande.");
            using var document = JsonDocument.Parse(reportJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return BadRequest("El informe debe ser un objeto JSON.");
            // Stateless export of the displayed report. This does not import,
            // authenticate, persist, or approve anything from the posted JSON.
            return File(Encoding.UTF8.GetBytes(document.RootElement.GetRawText()), "application/json; charset=utf-8", "listener-resultado.json");
        }
        catch (JsonException) { return BadRequest("El informe no es un JSON válido."); }
    }

    public async Task<IActionResult> OnPostGmailAsync(bool testOnly = false, bool activate = false)
    {
        var password = GmailConnectionTest.NormalizeAppPassword(AppPassword ?? "");
        // Never echo the bound password back through Razor's ModelState.
        AppPassword = null;
        ModelState.Remove(nameof(AppPassword));
        Email = Email?.Trim() ?? "";
        if (!ModelState.IsValid || !GmailConnectionTest.IsPersonalGmailAddress(Email)
            || !GmailConnectionTest.HasAppPasswordFormat(password))
        {
            Failure = "Revisa el correo Gmail, la contraseña de aplicación de 16 caracteres, los días (1–90) y el límite (1–100).";
            return Page();
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            if (activate)
            {
                await GmailConnectionTest.CheckInboxAsync(Email, password, timeout.Token);
                automatic.Start(Email, password);
                Notice = "Lector automático activado. Espera al estado «Esperando correos nuevos» antes de enviar la primera prueba. Sigue funcionando aunque cierres esta página.";
                await LoadHistoryAsync();
            }
            else if (testOnly)
            {
                var count = await GmailConnectionTest.CheckInboxAsync(Email, password, timeout.Token);
                Notice = $"Conexión correcta. La bandeja contiene {count} mensajes. Puedes ejecutar una lectura de avisos BCP.";
            }
            else
            {
                Report = await GmailBcpReader.ReadReportAsync(Email, password, Days, Limit, timeout.Token);
                Notice = "Lectura finalizada. Gmail se ha desconectado; las marcas de lectura no se modificaron.";
            }
        }
        catch (Exception exception) { Failure = GmailConnectionTest.GetSafeError(exception); }
        return Page();
    }

    public async Task<IActionResult> OnPostFilesAsync()
    {
        AppPassword = null;
        ModelState.Clear();
        if (Files.Count is < 1 or > 20 || Files.Any(f => f.Length is <= 0 or > BcpEmailCommand.MaximumMessageBytes
                || !Path.GetExtension(f.FileName).Equals(".eml", StringComparison.OrdinalIgnoreCase)))
        {
            Failure = "Selecciona entre 1 y 20 archivos .eml, de hasta 1 MiB cada uno y no vacíos.";
            return Page();
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var items = new List<EmailReadItem>();
        foreach (var file in Files)
        {
            var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
            try
            {
                await using var stream = file.OpenReadStream();
                items.Add(new(name, await BcpEmailCommand.InspectAsync(stream, timeout.Token)));
            }
            catch (OperationCanceledException) { items.Add(new(name, Error: "LECTURA_CANCELADA")); }
            catch (Exception) { items.Add(new(name, Error: "ARCHIVO_NO_PROCESABLE")); }
        }
        Report = EmailReadReport.Create(items);
        Notice = "Análisis finalizado. Los archivos se procesaron sin guardarlos en el proyecto ni en SQL Server.";
        return Page();
    }
}
