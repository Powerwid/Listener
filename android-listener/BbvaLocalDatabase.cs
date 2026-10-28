using Android.Content;
using Android.Database.Sqlite;
using System.Text.Json;
using TonysGo.Listener.Bbva;

namespace TonysGo.YapeListener.Android;

internal sealed record BbvaCaptureResult(BbvaHistoryEntry Entry, bool NewPayment);

// One indexed row per notification: capture and the upload queue commit in the same transaction.
// Android's built-in SQLite avoids another native library and an ever-growing JSON snapshot.
internal sealed class BbvaLocalDatabase : SQLiteOpenHelper
{
    private readonly object _gate = new();
    private long _revision;
    public long Revision => Interlocked.Read(ref _revision);

    public BbvaLocalDatabase(Context context) : base(context, "bbva-listener.db", null, 1)
    {
        SetWriteAheadLoggingEnabled(true);
        _ = WritableDatabase;
        MigrateLegacy(context);
        lock (_gate)
        {
            foreach (var entry in Query("status=?", [(int)BbvaDeliveryStatus.Sending], null))
                SetStatus(entry.Notification.NotificationKey, BbvaDeliveryStatus.Retry, "Envío interrumpido; pendiente de reintento.");
        }
    }

    public override void OnCreate(SQLiteDatabase? db)
    {
        db!.ExecSQL("CREATE TABLE notices(event_id TEXT PRIMARY KEY NOT NULL, posted_at INTEGER NOT NULL, payment INTEGER NOT NULL, status INTEGER NOT NULL, entry_json TEXT NOT NULL, upload_device TEXT, alerted INTEGER NOT NULL DEFAULT 0, retry_at INTEGER NOT NULL DEFAULT 0)");
        db.ExecSQL("CREATE INDEX notices_pending ON notices(payment,status,retry_at)");
        db.ExecSQL("CREATE INDEX notices_recent ON notices(posted_at DESC)");
        db.ExecSQL("CREATE TABLE metadata(name TEXT PRIMARY KEY NOT NULL,value TEXT NOT NULL)");
    }
    public override void OnUpgrade(SQLiteDatabase? db, int oldVersion, int newVersion) { }

    public BbvaCaptureResult Capture(BbvaNotificationRequest notification)
    {
        if (notification.PackageName != BbvaNotificationParser.OfficialPackage) throw new ArgumentException("Solo se admite BBVA Empresas.");
        lock (_gate)
        {
            var db = WritableDatabase!;
            db.BeginTransaction();
            try
            {
                var previous = Find(notification.NotificationKey);
                var payment = BbvaNotificationParser.Parse(notification);
                if (previous is not null && (previous.Payment.EsAbono || !payment.EsAbono))
                {
                    db.SetTransactionSuccessful();
                    return new(previous, false);
                }
                var entry = new BbvaHistoryEntry(notification, payment, previous?.CapturedAt ?? DateTimeOffset.UtcNow,
                    payment.EsAbono ? BbvaDeliveryStatus.Pending : BbvaDeliveryStatus.HistoryOnly);
                Write(entry, previous is not null);
                db.SetTransactionSuccessful();
                return new(entry, payment.EsAbono);
            }
            finally { db.EndTransaction(); }
        }
    }

    public IReadOnlyList<BbvaHistoryEntry> Read() { lock (_gate) return Query(null, [], 200); }
    public BbvaHistoryEntry? Find(string id) { lock (_gate) return Query("event_id=?", [id], 1).FirstOrDefault(); }
    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                using var cursor = ReadableDatabase!.RawQuery("SELECT COUNT(*) FROM notices WHERE payment=1 AND status<>?", [((int)BbvaDeliveryStatus.StoredByListener).ToString()]);
                return cursor!.MoveToFirst() ? cursor.GetInt(0) : 0;
            }
        }
    }

    public IReadOnlyList<BbvaHistoryEntry> Pending(bool retryRejected = false, bool dueOnly = false)
    {
        lock (_gate)
        {
            var where = "payment=1 AND status<>?";
            var args = new List<object> { (int)BbvaDeliveryStatus.StoredByListener };
            if (!retryRejected) { where += " AND status<>?"; args.Add((int)BbvaDeliveryStatus.Rejected); }
            if (dueOnly) { where += " AND retry_at<=?"; args.Add(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); }
            return Query(where, args.ToArray(), dueOnly ? 30 : null, oldestFirst: true);
        }
    }

    public DateTimeOffset? NextRetry()
    {
        lock (_gate)
        {
            using var cursor = ReadableDatabase!.RawQuery("SELECT MIN(retry_at) FROM notices WHERE payment=1 AND status=?", [((int)BbvaDeliveryStatus.Retry).ToString()]);
            return cursor!.MoveToFirst() && !cursor.IsNull(0) ? DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(0)) : null;
        }
    }

    public bool IsDue(string id)
    {
        lock (_gate)
        {
            using var cursor = ReadableDatabase!.RawQuery("SELECT retry_at FROM notices WHERE event_id=?", [id]);
            return cursor!.MoveToFirst() && cursor.GetLong(0) <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    public void SetStatus(string id, BbvaDeliveryStatus status, string detail)
    {
        lock (_gate)
        {
            var previous = Find(id);
            if (previous is null || previous.Status == BbvaDeliveryStatus.StoredByListener) return;
            var entry = BbvaDeliveryRules.Transition(previous, status, detail, DateTimeOffset.UtcNow);
            Write(entry, true, status == BbvaDeliveryStatus.Retry
                ? DateTimeOffset.UtcNow.Add(BbvaDeliveryRules.RetryDelay(entry.Attempts)).ToUnixTimeMilliseconds() : 0L);
        }
    }

    public BbvaNotificationRequest BindDevice(string id, string device)
    {
        lock (_gate)
        {
            var entry = Find(id) ?? throw new FileNotFoundException("Aviso no encontrado.");
            using var cursor = ReadableDatabase!.RawQuery("SELECT upload_device FROM notices WHERE event_id=?", [id]);
            cursor!.MoveToFirst();
            var bound = cursor.IsNull(0) ? device : cursor.GetString(0)!;
            if (cursor.IsNull(0))
            {
                using var values = new ContentValues(); values.Put("upload_device", bound);
                WritableDatabase!.Update("notices", values, "event_id=?", [id]);
            }
            return entry.Notification with { DeviceId = bound };
        }
    }

    public bool NeedsAlert(string id)
    {
        lock (_gate)
        {
            using var cursor = ReadableDatabase!.RawQuery("SELECT alerted FROM notices WHERE event_id=?", [id]);
            return cursor!.MoveToFirst() && cursor.GetInt(0) == 0;
        }
    }
    public void MarkAlerted(string id)
    {
        lock (_gate)
        {
            using var values = new ContentValues(); values.Put("alerted", 1);
            WritableDatabase!.Update("notices", values, "event_id=?", [id]);
        }
    }

    private void Write(BbvaHistoryEntry entry, bool update, long? retryAt = null)
    {
        using var values = new ContentValues();
        values.Put("event_id", entry.Notification.NotificationKey);
        values.Put("posted_at", entry.Notification.PostedAt.ToUnixTimeMilliseconds());
        values.Put("payment", entry.Payment.EsAbono ? 1 : 0);
        values.Put("status", (int)entry.Status);
        values.Put("entry_json", JsonSerializer.Serialize(entry, BbvaDeviceJsonContext.Default.BbvaHistoryEntry));
        if (retryAt is long retry) values.Put("retry_at", retry);
        var db = WritableDatabase!;
        if (update) db.Update("notices", values, "event_id=?", [entry.Notification.NotificationKey]);
        else db.InsertOrThrow("notices", null, values);
        Interlocked.Increment(ref _revision);
    }
    private IReadOnlyList<BbvaHistoryEntry> Query(string? where, object[] args, int? limit, bool oldestFirst = false)
    {
        var sql = "SELECT event_id,entry_json FROM notices" + (where is null ? "" : " WHERE " + where)
            + (oldestFirst ? " ORDER BY posted_at,event_id" : " ORDER BY posted_at DESC,event_id") + (limit is int max ? " LIMIT " + max : "");
        using var cursor = ReadableDatabase!.RawQuery(sql, args.Select(x => Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture)!).ToArray());
        var entries = new List<BbvaHistoryEntry>();
        while (cursor!.MoveToNext())
        {
            try
            {
                var entry = JsonSerializer.Deserialize(cursor.GetString(1)!, BbvaDeviceJsonContext.Default.BbvaHistoryEntry);
                if (entry?.Notification is not null && entry.Payment is not null) entries.Add(entry);
            }
            catch (JsonException)
            {
                // Preserve the raw data but remove a damaged row from automatic retries.
                using var values = new ContentValues(); values.Put("status", (int)BbvaDeliveryStatus.Rejected);
                WritableDatabase!.Update("notices", values, "event_id=?", [cursor.GetString(0)!]);
            }
        }
        return entries;
    }

    private void MigrateLegacy(Context context)
    {
        var db = WritableDatabase!;
        using (var cursor = db.RawQuery("SELECT value FROM metadata WHERE name='legacy-v1'", []))
            if (cursor!.MoveToFirst()) return;
        var root = context.FilesDir!.AbsolutePath;
        var imports = new List<BbvaHistoryEntry>();
        var historyPath = Path.Combine(root, "bbva-history.json");
        if (File.Exists(historyPath))
        {
            try { imports.AddRange(JsonSerializer.Deserialize(File.ReadAllText(historyPath), BbvaDeviceJsonContext.Default.ListBbvaHistoryEntry) ?? []); }
            catch (Exception e) when (e is JsonException or IOException)
            { ListenerDebugLog.Warning(context, "El historial JSON anterior no se pudo leer; se conserva y se recuperará la cola independiente."); }
        }
        var queued = new List<BbvaQueuedUpload>();
        var outbox = Path.Combine(root, "bbva-outbox");
        if (Directory.Exists(outbox))
            foreach (var path in Directory.EnumerateFiles(outbox, "*.json"))
            {
                try
                {
                    var item = JsonSerializer.Deserialize(File.ReadAllText(path), BbvaDeviceJsonContext.Default.BbvaQueuedUpload);
                    if (item?.Notification is not null) queued.Add(item);
                }
                catch (Exception e) when (e is JsonException or IOException)
                { ListenerDebugLog.Warning(context, "Un archivo de la cola anterior no se pudo leer; se conserva para revisión."); }
            }
        db.BeginTransaction();
        try
        {
            foreach (var entry in imports.Where(x => x is not null && x.Notification is not null && x.Payment is not null))
                if (Find(entry.Notification.NotificationKey) is null) Write(entry, false);
            foreach (var item in queued)
            {
                if (Find(item.Notification.NotificationKey) is null) Capture(item.Notification);
                if (item.UploadDeviceId is { } device) BindDevice(item.Notification.NotificationKey, device);
            }
            db.ExecSQL("INSERT INTO metadata(name,value) VALUES('legacy-v1','done')");
            db.SetTransactionSuccessful();
        }
        finally { db.EndTransaction(); }
        // Keep legacy files intact; the metadata marker makes the migration one-time.
    }
}
