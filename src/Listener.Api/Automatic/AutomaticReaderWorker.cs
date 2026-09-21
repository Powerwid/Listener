using Listener.Api.Diagnostics;

namespace Listener.Api.Automatic;

public sealed class AutomaticReaderWorker(AutomaticState state, IAutomaticMailbox mailbox, NotificationStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var credentials = state.Credentials;
            if (credentials is not null)
            {
                using var cycle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, credentials.Cancellation);
                cycle.CancelAfter(TimeSpan.FromSeconds(90));
                try
                {
                    var cursor = await store.GetCheckpointAsync(credentials.Email, cycle.Token);
                    var poll = await mailbox.PollAsync(credentials, cursor, cycle.Token);
                    var count = await store.SaveAsync(credentials.Email, cursor, poll, cycle.Token);
                    state.Complete(credentials, count, poll.Initialized);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested || credentials.Cancellation.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    var message = exception is InvalidOperationException { Message: "UIDVALIDITY_CAMBIO_REQUIERE_REVISION" }
                        ? "Cambió la identidad de la carpeta. Se necesita revisar el cursor antes de continuar."
                        : exception is MailKit.Security.AuthenticationException
                            ? GmailConnectionTest.GetSafeError(exception)
                            : "No se completó la sincronización. Revisa Gmail, SQL Server y las migraciones. Se reintentará sin adelantar el cursor.";
                    state.Fail(credentials, message);
                }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(credentials is null ? 2 : 30), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
