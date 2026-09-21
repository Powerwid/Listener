using Listener.Api.Email;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace Listener.Api.Automatic;

public sealed record MailCheckpoint(long? Epoch, long LastUid);
public sealed record IncomingMail(long Uid, DateTimeOffset ReceivedAt, byte[]? Raw, string? Error);
public sealed record MailPoll(long Epoch, long LastUid, bool Initialized, IReadOnlyList<IncomingMail> Messages);

public interface IAutomaticMailbox
{
    Task<MailPoll> PollAsync(MailboxCredentials credentials, MailCheckpoint checkpoint, CancellationToken ct);
}

public sealed class AutomaticMailbox : IAutomaticMailbox
{
    public async Task<MailPoll> PollAsync(MailboxCredentials credentials, MailCheckpoint checkpoint, CancellationToken ct)
    {
        using var client = new ImapClient { Timeout = 15000 };
        await client.ConnectAsync("imap.gmail.com", 993, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(credentials.Email, credentials.Password, ct);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, ct);
        var epoch = client.Inbox.UidValidity;
        var newestAtOpen = (long)(client.Inbox.UidNext?.Id ?? throw new InvalidOperationException("UIDNEXT_NO_DISPONIBLE")) - 1;
        if (checkpoint.Epoch is null)
        {
            // First activation starts at current Inbox, never treats old receipts as new payments.
            await client.DisconnectAsync(true, ct);
            return new(epoch, newestAtOpen, true, []);
        }
        if (checkpoint.Epoch != epoch)
            throw new InvalidOperationException("UIDVALIDITY_CAMBIO_REQUIERE_REVISION");
        var messages = new List<IncomingMail>();
        var last = checkpoint.LastUid;
        if (last < uint.MaxValue)
        {
            var range = new UniqueIdRange(new UniqueId((uint)(last + 1)), UniqueId.MaxValue);
            var uids = await client.Inbox.SearchAsync(SearchQuery.Uids(range).And(SearchQuery.FromContains(BcpEmailParser.SenderAddress)), ct);
            var selected = uids.Where(u => u.Id > last).OrderBy(u => u.Id).Take(100).ToList();
            if (selected.Count > 0)
            {
                var summaries = await client.Inbox.FetchAsync(selected, MessageSummaryItems.UniqueId | MessageSummaryItems.Size | MessageSummaryItems.InternalDate, ct);
                foreach (var uid in selected)
                {
                    var summary = summaries.FirstOrDefault(s => s.UniqueId == uid);
                    var received = summary?.InternalDate ?? DateTimeOffset.UtcNow;
                    if (summary is null || summary.Size is null or > BcpEmailCommand.MaximumMessageBytes)
                        messages.Add(new(uid.Id, received, null, summary is null ? "MENSAJE_NO_DISPONIBLE" : "TAMANO_NO_PERMITIDO"));
                    else
                    {
                        try
                        {
                            using var stream = await client.Inbox.GetStreamAsync(uid, 0, BcpEmailCommand.MaximumMessageBytes + 1, ct);
                            using var buffer = new MemoryStream();
                            await stream.CopyToAsync(buffer, ct);
                            messages.Add(buffer.Length > BcpEmailCommand.MaximumMessageBytes
                                ? new(uid.Id, received, null, "TAMANO_NO_PERMITIDO")
                                : new(uid.Id, received, buffer.ToArray(), null));
                        }
                        catch (MessageNotFoundException) { messages.Add(new(uid.Id, received, null, "MENSAJE_NO_DISPONIBLE")); }
                    }
                    last = uid.Id;
                }
            }
            else last = Math.Max(last, newestAtOpen);
        }
        await client.DisconnectAsync(true, ct);
        return new(epoch, last, false, messages);
    }
}
