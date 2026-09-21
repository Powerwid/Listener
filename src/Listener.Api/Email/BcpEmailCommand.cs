using System.Text.Json;
using MimeKit;

namespace Listener.Api.Email;

public static class BcpEmailCommand
{
    public const int MaximumMessageBytes = 1024 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 2 || args[0] != "--inspect-bcp-eml")
        {
            Console.Error.WriteLine("Usa --inspect-bcp-eml seguido de la ruta absoluta del archivo .eml entre comillas.");
            return 2;
        }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var stream = File.OpenRead(args[1]);
            var result = await InspectAsync(stream, timeout.Token);
            Console.WriteLine(JsonSerializer.Serialize(new { origen = "ArchivoLocal", resultado = result }, JsonOptions));
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("No se pudo analizar el EML. Verifica la ruta, el formato MIME y el limite de 1 MiB. No se guardaron datos.");
            return 1;
        }
    }

    public static async Task<int> RunBatchAsync(string[] args)
    {
        if (args.Length is < 2 or > 21 || args[0] != "--inspect-bcp-emls"
            || args.Skip(1).Any(a => a.StartsWith("--", StringComparison.Ordinal)))
        {
            Console.Error.WriteLine("Usa --inspect-bcp-emls seguido de 1 a 20 rutas absolutas .eml entre comillas.");
            return 2;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var report = await InspectFilesAsync(args.Skip(1).ToArray(), timeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return report.Errores.Count > 0 ? 1 : 0;
    }

    public static async Task<EmailReadReport> InspectFilesAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(paths));
        var items = new List<EmailReadItem>();
        foreach (var path in paths)
        {
            // Do not expose private absolute directory paths in shared JSON output.
            var reference = Path.GetFileName(path);
            try
            {
                ct.ThrowIfCancellationRequested();
                await using var stream = File.OpenRead(path);
                items.Add(new(reference, await InspectAsync(stream, ct)));
            }
            catch (OperationCanceledException) { items.Add(new(reference, Error: "LECTURA_CANCELADA")); }
            catch (IOException) { items.Add(new(reference, Error: "ARCHIVO_NO_DISPONIBLE")); }
            catch (UnauthorizedAccessException) { items.Add(new(reference, Error: "ARCHIVO_NO_DISPONIBLE")); }
            catch (FormatException) { items.Add(new(reference, Error: "FORMATO_O_TAMANO_NO_VALIDO")); }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { items.Add(new(reference, Error: "FORMATO_DEMASIADO_COMPLEJO")); }
            catch (ArgumentException) { items.Add(new(reference, Error: "RUTA_O_FORMATO_NO_VALIDO")); }
        }
        return EmailReadReport.Create(items);
    }

    public static async Task<BcpEmailResult> InspectAsync(Stream stream, CancellationToken ct)
    {
        // Bound the actual bytes read, including non-seekable network streams.
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, MaximumMessageBytes + 1 - (int)buffer.Length)), ct)) > 0)
        {
            buffer.Write(chunk, 0, count);
            if (buffer.Length > MaximumMessageBytes) throw new FormatException("Correo demasiado grande.");
        }
        if (buffer.Length == 0) throw new FormatException("Correo vacio.");
        buffer.Position = 0;
        using var message = await MimeMessage.LoadAsync(buffer, ct);
        return BcpEmailParser.Parse(message);
    }
}
