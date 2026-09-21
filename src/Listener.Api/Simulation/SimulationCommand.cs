using System.Text;
using System.Text.Json;
using Listener.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Listener.Api.Simulation;

public static class SimulationCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var flags = ParseArguments(args);
            if (!flags.TryGetValue("--environment", out var environment) || environment != "Development")
                throw new SimulationInputException("SOLO_DESARROLLO", "Agrega --environment Development. Este comando no se usa en produccion.");
            var configuration = new ConfigurationBuilder().AddUserSecrets<ListenerDbContext>().AddEnvironmentVariables().Build();
            var connection = configuration.GetConnectionString("ListenerDb")
                ?? throw new SimulationInputException("SIN_CONEXION", "Configura ConnectionStrings:ListenerDb en User Secrets.");
            EnsureLocalDatabase(connection);
            var options = new DbContextOptionsBuilder<ListenerDbContext>()
                .UseSqlServer(connection, sql => sql.EnableRetryOnFailure()).Options;
            var service = new SimulationService(options);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            SimulationResult result;
            if (flags.TryGetValue("--simulation-result", out var reference))
            {
                result = await service.GetAsync(reference, timeout.Token);
            }
            else
            {
                var json = await ReadLimitedAsync(Required(flags, "--solicitud"), 16384, timeout.Token);
                var eml = await ReadLimitedAsync(Required(flags, "--correo"), 131072, timeout.Token);
                result = await service.ImportAsync(SimulationRequest.Parse(Encoding.UTF8.GetString(json).TrimStart('\uFEFF')), eml, timeout.Token);
            }
            Console.WriteLine(JsonSerializer.Serialize(result, SimulationJson.Options));
            return 0;
        }
        catch (SimulationInputException exception)
        {
            return Error(exception.Code, exception.Message);
        }
        catch (JsonException)
        {
            return Error("JSON_INVALIDO", "Revisa el JSON: nombres de campos, comillas y monto numerico. No se aceptan campos adicionales.");
        }
        catch (FormatException)
        {
            return Error("CORREO_INVALIDO", "El archivo no tiene un formato de correo MIME valido.");
        }
        catch (OperationCanceledException)
        {
            return Error("TIEMPO_AGOTADO", "La operacion se cancelo o excedio 60 segundos. Consulta la referencia antes de repetirla.");
        }
        catch (IOException)
        {
            return Error("ARCHIVO_NO_DISPONIBLE", "No se pudo leer uno de los archivos locales.");
        }
        catch (Exception)
        {
            // Avoid leaking SQL connection details, MIME contents or secrets.
            return Error("PRUEBA_NO_COMPLETADA", "Revisa SQL Server y las migraciones. Si hubo una interrupcion, consulta la referencia antes de repetirla.");
        }
    }

    public static void EnsureLocalDatabase(string connection)
    {
        var builder = new SqlConnectionStringBuilder(connection);
        var allowed = new[] { @".\SQLEXPRESS", @"(local)\SQLEXPRESS", @"localhost\SQLEXPRESS", $@"{Environment.MachineName}\SQLEXPRESS" };
        if (!allowed.Contains(builder.DataSource, StringComparer.OrdinalIgnoreCase)
            || builder.InitialCatalog != "ListenerDb" || !builder.IntegratedSecurity)
            throw new SimulationInputException("DESTINO_NO_PERMITIDO", "El simulador solo permite ListenerDb en SQLEXPRESS local con autenticacion de Windows.");
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (key is not ("--simulate-payment" or "--simulation-result" or "--solicitud" or "--correo" or "--environment")
                || flags.ContainsKey(key))
                throw new SimulationInputException("ARGUMENTOS_INVALIDOS", "Opcion desconocida o repetida. Consulta docs/simulacion.md.");
            var value = "";
            if (key != "--simulate-payment")
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new SimulationInputException("ARGUMENTOS_INVALIDOS", "Falta el valor de una opcion.");
                value = args[i];
            }
            flags.Add(key, value);
        }
        if (flags.ContainsKey("--simulate-payment") == flags.ContainsKey("--simulation-result")
            || (flags.ContainsKey("--simulation-result") && (flags.ContainsKey("--solicitud") || flags.ContainsKey("--correo"))))
            throw new SimulationInputException("ARGUMENTOS_INVALIDOS", "Usa solo uno de los modos: simular o consultar.");
        return flags;
    }

    private static string Required(Dictionary<string, string> flags, string name) =>
        flags.TryGetValue(name, out var value) ? value : throw new SimulationInputException("ARGUMENTOS_INVALIDOS", $"Falta {name}.");

    private static async Task<byte[]> ReadLimitedAsync(string path, int maximum, CancellationToken ct)
    {
        await using var stream = new FileStream(ResolveInputPath(path), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is 0 || stream.Length > maximum)
            throw new SimulationInputException("ARCHIVO_INVALIDO", $"El archivo esta vacio o supera el limite de {maximum} bytes.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, ct);
        return bytes;
    }

    private static string ResolveInputPath(string path)
    {
        var direct = Path.GetFullPath(path);
        if (Path.IsPathRooted(path) || File.Exists(direct))
            return direct;
        // dotnet run sets the working directory to the project folder. Also accept
        // paths relative to the solution, as shown in the user-facing commands.
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Listener.sln")))
                return Path.GetFullPath(path, directory.FullName);
        }
        return direct;
    }

    private static int Error(string code, string message)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new { esSimulacion = true, pagoVerificado = false, error = code, mensaje = message }, SimulationJson.Options));
        return 1;
    }
}
