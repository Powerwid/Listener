namespace Listener.Api.Automatic;

public sealed record AutomaticStatus(bool Activo, string? Buzon, string Estado,
    DateTimeOffset? UltimaConsulta, int Guardados, string? Error, int IntervaloSegundos = 30);

public sealed record MailboxCredentials(string Email, string Password, CancellationToken Cancellation);

// Credentials live only in this server process; never in a response, database or log.
public sealed class AutomaticState : IDisposable
{
    private readonly object gate = new();
    private MailboxCredentials? credentials;
    private CancellationTokenSource? session;
    private AutomaticStatus status = new(false, null, "Detenido", null, 0, null);

    public AutomaticStatus Status { get { lock (gate) return status; } }
    public MailboxCredentials? Credentials { get { lock (gate) return credentials; } }

    public void Start(string email, string password)
    {
        lock (gate)
        {
            session?.Cancel();
            session?.Dispose();
            session = new();
            credentials = new(email.Trim().ToLowerInvariant(), password, session.Token);
            status = new(true, credentials.Email, "Conectando", null, 0, null);
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            session?.Cancel();
            credentials = null;
            status = status with { Activo = false, Estado = "Detenido" };
        }
    }

    public void Complete(MailboxCredentials current, int added, bool initialized)
    {
        lock (gate)
        {
            if (!ReferenceEquals(credentials, current)) return;
            status = status with { Estado = initialized ? "Esperando correos nuevos" : "Escuchando", UltimaConsulta = DateTimeOffset.UtcNow,
                Guardados = status.Guardados + added, Error = null };
        }
    }

    public void Fail(MailboxCredentials current, string error)
    {
        lock (gate)
        {
            if (ReferenceEquals(credentials, current))
                status = status with { Estado = "Reintentando", Error = error };
        }
    }

    public void Dispose() { Stop(); session?.Dispose(); }
}
