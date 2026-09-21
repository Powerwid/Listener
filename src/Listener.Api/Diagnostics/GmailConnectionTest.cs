using System.Net.Sockets;
using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using Listener.Api.Email;

namespace Listener.Api.Diagnostics;

/// <summary>A manual connectivity diagnostic, not the production mailbox worker.</summary>
public static class GmailConnectionTest
{
    public const string DefaultEmail = "Victorrmq35@gmail.com";

    public static async Task<int> RunAsync(bool readBcp = false)
    {
        if (Console.IsInputRedirected || (!readBcp && Console.IsOutputRedirected))
        {
            Console.Error.WriteLine("Ejecuta esta prueba en una terminal interactiva, sin redirigir la entrada o salida.");
            return 2;
        }

        // Leave stdout as a JSON document in read mode; prompts remain on the terminal.
        var progress = readBcp ? Console.Error : Console.Out;
        progress.WriteLine("Prueba local de Gmail. No envia, elimina ni marca mensajes como leidos.");
        progress.WriteLine("No guarda la contrasena. Pulsa Escape para cancelar al introducirla.");
        if (readBcp)
            progress.WriteLine("Lee hasta 20 correos BCP de los ultimos 30 dias en Inbox y muestra sus datos en JSON. No verifica pagos ni guarda datos en SQL.");
        progress.Write($"Correo Gmail [{DefaultEmail}]: ");
        var email = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(email))
            email = DefaultEmail;

        if (!IsPersonalGmailAddress(email))
        {
            Console.Error.WriteLine("Introduce solo una direccion personal @gmail.com o @googlemail.com.");
            return 2;
        }

        progress.Write("Contrasena de APLICACION de Google (entrada oculta, no tu contrasena habitual): ");
        var password = ReadHiddenPassword(progress);
        if (password is null)
        {
            progress.WriteLine("Prueba cancelada.");
            return 2;
        }

        password = NormalizeAppPassword(password);
        if (!HasAppPasswordFormat(password))
        {
            Console.Error.WriteLine("Se esperan los 16 caracteres de una contrasena de aplicacion de Google.");
            return 2;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            progress.WriteLine("Conectando a Gmail mediante TLS...");
            if (readBcp)
                await GmailBcpReader.ReadOnceAsync(email, password, timeout.Token);
            else
            {
                var count = await CheckInboxAsync(email, password, timeout.Token);
                Console.WriteLine($"CONEXION CORRECTA. Bandeja abierta en modo lectura. Mensajes en Inbox: {count}.");
            }
            progress.WriteLine("Conexion cerrada. Esta prueba no deja el lector funcionando.");
            return 0;
        }
        catch (Exception exception)
        {
            // Never print raw exceptions or protocol traces: they may include private data.
            Console.Error.WriteLine(GetSafeError(exception));
            return 1;
        }
    }

    public static async Task<int> CheckInboxAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (!IsPersonalGmailAddress(email) || !HasAppPasswordFormat(password))
            throw new ArgumentException("Configuracion de prueba no valida.");

        cancellationToken.ThrowIfCancellationRequested();
        using var client = new ImapClient { Timeout = 15000 };
        // Keep the default certificate validation. Never bypass TLS errors.
        await client.ConnectAsync("imap.gmail.com", 993, SecureSocketOptions.SslOnConnect, cancellationToken);
        await client.AuthenticateAsync(email, password, cancellationToken);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        var count = client.Inbox.Count;
        await client.DisconnectAsync(true, cancellationToken);
        return count;
    }

    public static bool IsPersonalGmailAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace))
            return false;

        return MailboxAddress.TryParse(value, out var address)
            && string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase)
            && (value.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("@googlemail.com", StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeAppPassword(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character)));

    public static bool HasAppPasswordFormat(string value) =>
        value.Length == 16 && value.All(char.IsAsciiLetterOrDigit);

    public static string GetSafeError(Exception exception) => exception switch
    {
        MailKit.Security.AuthenticationException =>
            "Google rechazo el acceso. Revisa la cuenta y su contrasena de aplicacion; no uses la habitual. Si tu cuenta no permite este metodo, usaremos OAuth.",
        SslHandshakeException =>
            "No se pudo establecer una conexion TLS valida. Revisa la fecha del equipo, la red y sus certificados; no desactives su validacion.",
        OperationCanceledException => "La prueba se cancelo o supero el limite de 45 segundos.",
        SocketException or IOException =>
            "Fallo de red. Comprueba internet y el acceso a imap.gmail.com por el puerto 993.",
        _ => "La prueba no se completo. No se mostraron detalles del servidor para proteger tus datos."
    };

    private static string? ReadHiddenPassword(TextWriter progress)
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Escape)
            {
                buffer.Clear();
                progress.WriteLine();
                return null;
            }
            if (key.Key == ConsoleKey.Enter)
            {
                progress.WriteLine();
                var result = buffer.ToString();
                buffer.Clear();
                return result;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                    buffer.Length--;
            }
            else if (!char.IsControl(key.KeyChar) && buffer.Length < 128)
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}
