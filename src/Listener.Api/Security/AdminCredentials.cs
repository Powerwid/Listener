using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Listener.Api.Security;

public sealed record AdminLogin(string Username, string PasswordHash);

public sealed class AdminCredentials(IConfiguration configuration, IHostEnvironment environment)
{
    public const string Scheme = "ListenerAdmin";
    private static readonly PasswordHasher<string> Hasher = new(Options.Create(new PasswordHasherOptions { IterationCount = 600_000 }));
    public static string FilePath(string root) => Path.Combine(root, "App_Data", "admin-login.json");
    public AdminLogin? Load()
    {
        if (configuration["ListenerAdmin:PasswordHash"] is { Length: > 0 } hash)
            return new(configuration["ListenerAdmin:Username"] ?? "admin", hash);
        var path = FilePath(environment.ContentRootPath);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<AdminLogin>(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
    }
    public bool Verify(string? username, string? password)
    {
        var login = Load();
        if (login is null || password is null || password.Length > 256) return false;
        try
        {
            var valid = Hasher.VerifyHashedPassword(login.Username, login.PasswordHash, password) != PasswordVerificationResult.Failed;
            return valid && string.Equals(username, login.Username, StringComparison.Ordinal);
        }
        catch (FormatException) { return false; }
    }
    public static string HashPassword(string password) => Hasher.HashPassword("admin", password);
    public string? Version => Load() is { } login ? Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(login.PasswordHash))) : null;
}

public static class AdminSetupCommand
{
    public static int Run(string root)
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Ejecuta --configure-admin en una terminal interactiva para crear la contrasena sin mostrarla.");
            return 2;
        }
        Console.Write("Contrasena del administrador (8 a 256 caracteres): ");
        var password = ReadPassword();
        Console.Write("Repite la contrasena: ");
        var confirmation = ReadPassword();
        if (password.Length is < 8 or > 256 || password != confirmation)
        {
            Console.Error.WriteLine("Las contrasenas deben coincidir y tener entre 8 y 256 caracteres.");
            return 2;
        }
        var path = AdminCredentials.FilePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new AdminLogin("admin", AdminCredentials.HashPassword(password))));
        File.Move(path + ".tmp", path, overwrite: true);
        Console.WriteLine("Administrador 'admin' configurado. Se guardo solo el hash de la contrasena en App_Data.");
        return 0;
    }
    private static string ReadPassword()
    {
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return new(chars.ToArray()); }
            if (key.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); }
            else if (!char.IsControl(key.KeyChar) && chars.Count < 257) chars.Add(key.KeyChar);
        }
    }
}
