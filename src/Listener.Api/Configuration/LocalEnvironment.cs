namespace Listener.Api.Configuration;

internal static class LocalEnvironment
{
    public static void Load()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Listener.Api", ".env"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".env"))
        };

        var path = candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists);
        if (path is null)
        {
            return;
        }

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length == 0 || Environment.GetEnvironmentVariable(key) is not null)
            {
                continue;
            }

            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
