using System.Text.Json;
using System.Text.Json.Serialization;

namespace TonysGo.Listener.Bbva;

public enum BbvaDeliveryStatus { HistoryOnly, Pending, Sending, StoredByListener, Retry, Rejected, ConfigurationRequired }

public sealed record BbvaHistoryEntry(
    BbvaNotificationRequest Notification, BbvaPaymentData Payment,
    DateTimeOffset CapturedAt, BbvaDeliveryStatus Status, string? Detail = null,
    int Attempts = 0, DateTimeOffset? LastAttemptAt = null);

/// <summary>Private device history. Writes atomically replace a complete snapshot.</summary>
public sealed class BbvaHistoryStore
{
    public const int MaximumEntries = 200;
    private readonly object _gate = new();
    private readonly string _path;
    private List<BbvaHistoryEntry> _entries;
    private long _revision;

    public BbvaHistoryStore(string path)
    {
        _path = path;
        _entries = File.Exists(path)
            ? JsonSerializer.Deserialize(File.ReadAllText(path), BbvaDeviceJsonContext.Default.ListBbvaHistoryEntry) ?? [] : [];
        _entries = _entries.Select(x => x.Status == BbvaDeliveryStatus.Sending
            ? x with { Status = BbvaDeliveryStatus.Retry, Detail = "Envio interrumpido; pendiente de reintento." } : x).ToList();
    }
    public long Revision { get { lock (_gate) return _revision; } }
    public IReadOnlyList<BbvaHistoryEntry> Read() { lock (_gate) return _entries.ToArray(); }

    public BbvaHistoryEntry Capture(BbvaNotificationRequest notification)
    {
        if (notification.PackageName != BbvaNotificationParser.OfficialPackage)
            throw new ArgumentException("El historial solo admite BBVA Empresas.", nameof(notification));
        lock (_gate)
        {
            var existing = _entries.FindIndex(x => x.Notification.NotificationKey == notification.NotificationKey);
            var payment = BbvaNotificationParser.Parse(notification);
            if (existing >= 0)
            {
                var previous = _entries[existing];
                // Preserve delivery and the first complete payment when Android republishes it.
                if (previous.Payment.EsAbono || !payment.EsAbono) return previous;
                var updated = previous with { Notification = notification, Payment = payment, Status = BbvaDeliveryStatus.Pending };
                _entries[existing] = updated;
                Save();
                return updated;
            }
            var entry = new BbvaHistoryEntry(notification, payment, DateTimeOffset.UtcNow,
                payment.EsAbono ? BbvaDeliveryStatus.Pending : BbvaDeliveryStatus.HistoryOnly);
            _entries.Add(entry);
            _entries = _entries.OrderByDescending(x => x.Notification.PostedAt).Take(MaximumEntries).ToList();
            Save();
            return entry;
        }
    }

    public void SetStatus(string eventId, BbvaDeliveryStatus status, string detail)
    {
        lock (_gate)
        {
            var index = _entries.FindIndex(x => x.Notification.NotificationKey == eventId);
            if (index < 0 || _entries[index].Status == BbvaDeliveryStatus.StoredByListener) return;
            var entry = _entries[index];
            _entries[index] = entry with
            {
                Status = status, Detail = detail,
                Attempts = entry.Attempts + (status == BbvaDeliveryStatus.Sending ? 1 : 0),
                LastAttemptAt = status == BbvaDeliveryStatus.Sending ? DateTimeOffset.UtcNow : entry.LastAttemptAt
            };
            Save();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_entries, BbvaDeviceJsonContext.Default.ListBbvaHistoryEntry));
        File.Move(temporary, _path, overwrite: true);
        _revision++;
    }
}

public sealed record BbvaQueuedUpload(BbvaNotificationRequest Notification, string? UploadDeviceId = null);

/// <summary>Outbox independent of the bounded history and WorkManager's 10 KB input limit.</summary>
public sealed class BbvaPendingUploads(string directory)
{
    private readonly object _gate = new();

    public void Add(BbvaNotificationRequest notification)
    {
        lock (_gate)
        {
            var path = EntryPath(notification.NotificationKey);
            if (!File.Exists(path)) Write(path, new BbvaQueuedUpload(notification));
        }
    }

    public BbvaQueuedUpload BindDevice(string eventId, string deviceId)
    {
        lock (_gate)
        {
            var path = EntryPath(eventId);
            var queued = JsonSerializer.Deserialize(File.ReadAllText(path), BbvaDeviceJsonContext.Default.BbvaQueuedUpload)
                ?? throw new InvalidDataException("El envio BBVA no contiene datos.");
            if (queued.UploadDeviceId is not null) return queued;
            queued = queued with { UploadDeviceId = deviceId };
            Write(path, queued);
            return queued;
        }
    }

    public IReadOnlyList<BbvaQueuedUpload> Read()
    {
        lock (_gate)
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.json").Select(path =>
                    JsonSerializer.Deserialize(File.ReadAllText(path), BbvaDeviceJsonContext.Default.BbvaQueuedUpload)
                    ?? throw new InvalidDataException("El envio BBVA no contiene datos.")).ToArray() : [];
    }

    public void Remove(string eventId) { lock (_gate) File.Delete(EntryPath(eventId)); }

    private string EntryPath(string eventId)
    {
        if (string.IsNullOrEmpty(eventId) || eventId.Length > 200 || eventId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Identificador BBVA invalido.", nameof(eventId));
        return Path.Combine(directory, eventId + ".json");
    }

    private void Write(string path, BbvaQueuedUpload queued)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(queued, BbvaDeviceJsonContext.Default.BbvaQueuedUpload));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}

// Keep persistence and HTTP serialization working in trimmed Android Release builds.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<BbvaHistoryEntry>))]
[JsonSerializable(typeof(BbvaQueuedUpload))]
[JsonSerializable(typeof(BbvaNotificationRequest))]
[JsonSerializable(typeof(BbvaIngestResult))]
public partial class BbvaDeviceJsonContext : JsonSerializerContext { }
