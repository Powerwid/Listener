using Android.Content;
using Android.Util;

namespace TonysGo.YapeListener.Android;

internal static class ListenerDebugLog
{
    private const string Tag = "TonysGoYapeListener";
    private const string PreferencesName = "listener_debug";
    private const string EntriesKey = "entries";
    private const int MaximumEntries = 80;

    public static void Info(Context context, string message) => Write(context, "INFO", message);
    public static void Warning(Context context, string message) => Write(context, "WARN", message);
    public static void Error(Context context, string message) => Write(context, "ERROR", message);

    public static string Read(Context context)
    {
        var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
        return preferences?.GetString(EntriesKey, "Sin eventos de depuracion.")
               ?? "Sin eventos de depuracion.";
    }

    public static void Clear(Context context)
    {
        var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
        using var editor = preferences?.Edit();
        editor?.Remove(EntriesKey);
        editor?.Apply();
        Log.Info(Tag, "Registro de depuracion limpiado.");
    }

    private static void Write(Context context, string level, string message)
    {
        var safeMessage = message.Length <= 500 ? message : message[..500];
        var line = $"{DateTimeOffset.Now:HH:mm:ss} [{level}] {safeMessage}";
        if (level == "ERROR") Log.Error(Tag, safeMessage);
        else if (level == "WARN") Log.Warn(Tag, safeMessage);
        else Log.Info(Tag, safeMessage);

        var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
        var current = preferences?.GetString(EntriesKey, string.Empty) ?? string.Empty;
        var entries = current.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        entries.Insert(0, line);
        if (entries.Count > MaximumEntries) entries.RemoveRange(MaximumEntries, entries.Count - MaximumEntries);
        using var editor = preferences?.Edit();
        editor?.PutString(EntriesKey, string.Join("\n", entries));
        editor?.Apply();
    }
}
