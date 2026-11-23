using System.Text;
using Edgepad.Trust;

namespace Edgepad.Dashboard;

/// <summary>
/// <c>integrations.txt</c>: which dashboard integrations are on and what Edgepad remembers about them, one <c>key=value</c> per
/// line. Everything is off until the owner turns it on: a file that is missing, unreadable, too large or not written by Edgepad
/// reads as all off, and only the value <c>1</c> is "on". No secret is ever stored here (the GitHub token lives in the OS
/// secret store). A change takes effect at once whatever the disk says; the return value says only whether it will survive a
/// restart. Keys this version does not know are kept when the file is written, so a newer build's settings are not lost.
/// </summary>
internal sealed class IntegrationSettings(string path)
{
    public const string FileName = "integrations.txt";
    public const int MaxCityLength = 60;

    public const string Stats = "stats";
    public const string Weather = "weather";
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string GitHub = "github";
    public const string Developer = "developer";
    public const string City = "city";
    public const string Lat = "lat";
    public const string Lon = "lon";
    public const string Place = "place";
    public const string GitHubLogin = "githubLogin";
    public const string CodexTrusted = "codexTrusted";

    private const int MaxFileBytes = 64 * 1024;
    private const int MaxValueLength = 300;
    private const int MaxKeyLength = 40;

    private readonly Lock gate = new();
    private readonly Dictionary<string, string> values = Load(path);

    public static string PluginVersionKey(string agent) => "pluginVersion." + agent;

    /// <summary>The app executable last stamped as <c>__APP__</c> for the agent.</summary>
    public static string PluginAppKey(string agent) => "pluginApp." + agent;

    /// <summary>Whether the key is on and, for the developer keys (every AI tool and git host), developer mode is on too.</summary>
    public bool Active(string key) => Flag(key) && (key is not ("claude" or "codex" or "gemini" or "opencode" or "github" or "gitlab" or "gitea" or "bitbucket" or "gitee") || Flag(Developer));

    /// <summary>True only when the stored value is exactly <c>1</c>.</summary>
    public bool Flag(string key)
    {
        lock (gate)
        {
            return values.TryGetValue(key, out var value) && value == "1";
        }
    }

    /// <summary>The stored text, or empty.</summary>
    public string Text(string key)
    {
        lock (gate)
        {
            return values.GetValueOrDefault(key, string.Empty);
        }
    }

    public bool SetFlag(string key, bool on) => Set(key, on ? "1" : "0");

    /// <summary>
    /// Stores <paramref name="value"/> with control characters turned to spaces, trimmed and cut (the city to 60 characters, anything
    /// else to 300, never inside a surrogate pair). An empty value removes the key. A key that is not letters, digits, dot, dash or underscore is ignored.
    /// </summary>
    public bool Set(string key, string value)
    {
        if (!ValidKey(key))
        {
            return false;
        }

        var clean = Cut(Flatten(value), key == City ? MaxCityLength : MaxValueLength);
        lock (gate)
        {
            if (clean.Length == 0)
            {
                values.Remove(key);
            }
            else
            {
                values[key] = clean;
            }

            // Written under the lock, so two changes at once cannot land on disk out of order.
            var text = string.Concat(values.Select(v => $"{v.Key}={v.Value}\n"));
            try
            {
                PrivateStorage.Replace(path, temp => File.WriteAllText(temp, text, new UTF8Encoding(false)));
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Write($"The integration settings could not be saved: {e.GetType().Name}");
                return false;
            }
        }
    }

    private static bool ValidKey(string key) =>
        key.Length is >= 1 and <= MaxKeyLength && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    private static string Flatten(string value)
    {
        var flat = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            flat.Append(Rune.IsControl(rune) ? " " : rune.ToString());
        }

        return flat.ToString().Trim();
    }

    private static string Cut(string text, int max)
    {
        var kept = new StringBuilder(text.Length);
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count++ >= max)
            {
                break;
            }

            kept.Append(rune.ToString());
        }

        return kept.ToString().TrimEnd();
    }

    private static Dictionary<string, string> Load(string file)
    {
        var loaded = new Dictionary<string, string>();
        try
        {
            if (!File.Exists(file))
            {
                return loaded;
            }

            var info = new FileInfo(file);
            if (info.Length > MaxFileBytes)
            {
                Log.Write("The integration settings file is too large, so every integration is off.");
                return loaded;
            }

            foreach (var raw in File.ReadAllText(file).Split('\n'))
            {
                var line = raw.Trim();
                var at = line.IndexOf('=', StringComparison.Ordinal);
                if (at <= 0 || line[0] == '#')
                {
                    continue;
                }

                var key = line[..at].Trim();
                var value = line[(at + 1)..].Trim();
                if (ValidKey(key) && value.Length > 0 && value.Length <= MaxValueLength)
                {
                    loaded[key] = value;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            Log.Write($"The integration settings could not be read, so every integration is off: {e.GetType().Name}");
            loaded.Clear();
        }

        return loaded;
    }
}
