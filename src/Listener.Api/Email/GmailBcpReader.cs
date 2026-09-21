using System.Text.Json;
using Listener.Api.Diagnostics;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace Listener.Api.Email;

public static class GmailBcpReader
{
    public static async Task ReadOnceAsync(string email, string password, CancellationToken ct)
    {
        var report = await ReadReportAsync(email, password, 30, 20, ct);
        Console.WriteLine(JsonSerializer.Serialize(report, BcpEmailCommand.JsonOptions));
    }

    public static async Task<EmailReadReport> ReadReportAsync(string email, string password, int days, int limit, CancellationToken ct)
    {
        if (days is < 1 or > 90 || limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (!GmailConnectionTest.IsPersonalGmailAddress(email) || !GmailConnectionTest.HasAppPasswordFormat(password))
            throw new ArgumentException("Configuracion no valida.");
        ct.ThrowIfCancellationRequested();
        using var client = new ImapClient { Timeout = 15000 };
        await client.ConnectAsync("imap.gmail.com", 993, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(email, password, ct);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, ct);
        var since = DateTime.UtcNow.AddDays(-days).Date;
        var matches = await client.Inbox.SearchAsync(SearchQuery.FromContains(BcpEmailParser.SenderAddress)
            .And(SearchQuery.DeliveredAfter(since)), ct);
        var selected = matches.OrderByDescending(uid => uid.Id).Take(limit).ToList();
        var results = new List<EmailReadItem>();
        if (selected.Count > 0)
        {
            var summaries = await client.Inbox.FetchAsync(selected, MessageSummaryItems.UniqueId | MessageSummaryItems.Size, ct);
            foreach (var item in summaries.Where(item => selected.Contains(item.UniqueId)).OrderByDescending(item => item.UniqueId.Id))
            {
                if (item.Size is null or > BcpEmailCommand.MaximumMessageBytes)
                {
                    results.Add(new(item.UniqueId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Error: "TAMANO_NO_PERMITIDO"));
                    continue;
                }
                try
                {
                    using var stream = await client.Inbox.GetStreamAsync(item.UniqueId, 0, BcpEmailCommand.MaximumMessageBytes + 1, ct);
                    var result = await BcpEmailCommand.InspectAsync(stream, ct);
                    results.Add(new(item.UniqueId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), result));
                }
                catch (MessageNotFoundException)
                {
                    results.Add(new(item.UniqueId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Error: "MENSAJE_YA_NO_DISPONIBLE"));
                }
                catch (FormatException)
                {
                    results.Add(new(item.UniqueId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Error: "FORMATO_NO_VALIDO"));
                }
                catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
                {
                    results.Add(new(item.UniqueId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Error: "FORMATO_DEMASIADO_COMPLEJO"));
                }
            }
            foreach (var uid in selected.Where(uid => summaries.All(item => item.UniqueId != uid)))
                results.Add(new(uid.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Error: "MENSAJE_YA_NO_DISPONIBLE"));
        }
        var epoch = client.Inbox.UidValidity;
        await client.DisconnectAsync(true, ct);
        var report = EmailReadReport.Create(results) with
        {
            Origen = "GmailImap", Buzon = email, Carpeta = "INBOX", UidValidity = epoch,
            Desde = since.ToString("yyyy-MM-dd"), CoincidenciasFiltro = matches.Count, Limite = limit
        };
        return report;
    }
}
