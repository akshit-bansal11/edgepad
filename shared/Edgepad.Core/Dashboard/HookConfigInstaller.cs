using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Edgepad.Protocol;
using Edgepad.Trust;

namespace Edgepad.Dashboard;

/// <summary>
/// Connect and Disconnect for Gemini CLI and OpenCode, the two tools with no plugin CLI: Edgepad writes its own entry into the
/// tool's config and takes only that entry out again. Gemini CLI: five hook entries named <c>edgepad</c> merged into
/// <c>~/.gemini/settings.json</c>; every other key, hook and group is left as parsed. OpenCode: the file <c>edgepad.ts</c> in
/// <c>~/.config/opencode/plugins/</c>, which a file without our first-line marker is never overwritten or deleted. A config that is
/// not strict JSON (comments, a trailing comma, a duplicate key, a root that is not an object) is refused and reported, never
/// rewritten: rewriting would lose what the parser dropped. The old file is copied to <c>settings.json.edgepad-backup</c> before
/// every write, and the new one replaces it whole (see <see cref="PrivateStorage"/>), keeping the mode of the file it replaces.
/// Unverified on a real install: Windows and macOS config locations (the docs name only <c>~/.gemini</c> and <c>~/.config/opencode</c>).
/// </summary>
internal sealed class HookConfigInstaller(IPluginSource bundle, IntegrationSettings settings, string home, string version, string appPath)
{
    public const string GeminiTemplate = "hooks/hooks.gemini.json";
    public const string OpenCodeTemplate = "opencode/edgepad.ts";
    public const string HookName = "edgepad";
    public const string OpenCodeMarker = "Edgepad OpenCode plugin";
    public const string BackupSuffix = ".edgepad-backup";

    private const int MaxBytes = 1024 * 1024;
    private const string GeminiCommand = "--hook gemini session";

    private static readonly string[] Agents = [AgentToken.Gemini, AgentToken.OpenCode];

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The Gemini CLI user settings file.</summary>
    public string GeminiFile => Path.Combine(home, ".gemini", "settings.json");

    /// <summary>The OpenCode plugin file Edgepad writes.</summary>
    public string OpenCodeFile => Path.Combine(home, ".config", "opencode", "plugins", "edgepad.ts");

    /// <summary>Connected or NotConnected, from the saved switch.</summary>
    public PluginState Status(string agent) => settings.Flag(agent) ? PluginState.Connected : PluginState.NotConnected;

    /// <summary>Writes our entry. <see cref="PluginState.Connected"/>, or <see cref="PluginState.Failed"/> (logged) when the config is refused or the disk said no.</summary>
    public PluginResult Connect(string agent)
    {
        if (!PluginInstaller.SafePath(appPath) || Array.IndexOf(Agents, agent) < 0)
        {
            return PluginResult.Of(PluginState.Failed);
        }

        try
        {
            if (agent == AgentToken.Gemini)
            {
                ConnectGemini();
            }
            else
            {
                ConnectOpenCode();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            Log.Write($"The {agent} integration could not be written: {e.GetType().Name}");
            return PluginResult.Of(PluginState.Failed);
        }

        settings.Set(IntegrationSettings.PluginVersionKey(agent), version);
        settings.Set(IntegrationSettings.PluginAppKey(agent), appPath);
        settings.SetFlag(agent, true);
        return PluginResult.Of(PluginState.Connected);
    }

    /// <summary>Turns the switch off at once (the hook server then ignores the tool), then removes only our entry. Failed when the file could not be read or changed.</summary>
    public PluginResult Disconnect(string agent)
    {
        settings.SetFlag(agent, false);
        try
        {
            if (agent == AgentToken.Gemini)
            {
                DisconnectGemini();
            }
            else if (agent == AgentToken.OpenCode)
            {
                DisconnectOpenCode();
            }
            else
            {
                return PluginResult.Of(PluginState.Failed);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            Log.Write($"The {agent} integration could not be removed: {e.GetType().Name}");
            return PluginResult.Of(PluginState.Failed);
        }

        settings.Set(IntegrationSettings.PluginVersionKey(agent), string.Empty);
        settings.Set(IntegrationSettings.PluginAppKey(agent), string.Empty);
        return PluginResult.Of(PluginState.NotConnected);
    }

    /// <summary>A connected tool whose entry was written by another app version or for an app at another path is written again; reported <see cref="PluginState.RestartSessions"/>, or Failed.</summary>
    public IReadOnlyList<(string Agent, PluginState State)> EnsureCurrent()
    {
        var notices = new List<(string, PluginState)>();
        foreach (var agent in Agents)
        {
            if (!settings.Flag(agent)
                || (settings.Text(IntegrationSettings.PluginVersionKey(agent)) == version && settings.Text(IntegrationSettings.PluginAppKey(agent)) == appPath))
            {
                continue;
            }

            notices.Add((agent, Connect(agent).State == PluginState.Connected ? PluginState.RestartSessions : PluginState.Failed));
        }

        return notices;
    }

    private void ConnectGemini()
    {
        var ours = JsonNode.Parse(Template(GeminiTemplate))?["hooks"] as JsonObject ?? throw new InvalidDataException("The Gemini template has no hooks");
        var (root, existed) = LoadGemini();
        var before = root.ToJsonString(Pretty);
        RemoveOurs(root);
        if (root["hooks"] is null)
        {
            root["hooks"] = new JsonObject();
        }

        var hooks = root["hooks"] as JsonObject ?? throw new InvalidDataException("hooks is not an object");
        foreach (var (name, groups) in ours.ToList())
        {
            if (hooks[name] is null)
            {
                hooks[name] = new JsonArray();
            }

            var list = hooks[name] as JsonArray ?? throw new InvalidDataException("A hooks entry is not an array");
            foreach (var group in groups!.AsArray())
            {
                list.Add(group!.DeepClone());
            }
        }

        Save(GeminiFile, root, existed, before);
    }

    private void DisconnectGemini()
    {
        if (!File.Exists(GeminiFile))
        {
            return;
        }

        var (root, _) = LoadGemini();
        var before = root.ToJsonString(Pretty);
        RemoveOurs(root);
        Save(GeminiFile, root, existed: true, before);
    }

    private (JsonObject Root, bool Existed) LoadGemini()
    {
        if (!File.Exists(GeminiFile))
        {
            return (new JsonObject(), false);
        }

        if (new FileInfo(GeminiFile).Length > MaxBytes)
        {
            throw new InvalidDataException("The Gemini settings file is too large");
        }

        return (JsonNode.Parse(File.ReadAllText(GeminiFile)) as JsonObject ?? throw new InvalidDataException("The Gemini settings are not an object"), true);
    }

    // Takes out every hook entry named edgepad that runs our command, then the groups and event lists that this emptied; nothing else.
    private static void RemoveOurs(JsonObject root)
    {
        if (root["hooks"] is not JsonObject hooks)
        {
            return;
        }

        foreach (var (name, node) in hooks.ToList())
        {
            if (node is not JsonArray groups)
            {
                continue;
            }

            var touched = false;
            for (var i = groups.Count - 1; i >= 0; i--)
            {
                if (groups[i] is not JsonObject group || group["hooks"] is not JsonArray list)
                {
                    continue;
                }

                var count = list.Count;
                for (var j = list.Count - 1; j >= 0; j--)
                {
                    if (IsOurs(list[j]))
                    {
                        list.RemoveAt(j);
                    }
                }

                if (list.Count < count)
                {
                    touched = true;
                    if (list.Count == 0)
                    {
                        groups.RemoveAt(i);
                    }
                }
            }

            if (touched && groups.Count == 0)
            {
                hooks.Remove(name);
            }
        }

        if (hooks.Count == 0)
        {
            root.Remove("hooks");
        }
    }

    private static bool IsOurs(JsonNode? hook) =>
        hook is JsonObject h
        && h["name"] is JsonValue n && n.TryGetValue<string>(out var name) && name == HookName
        && h["command"] is JsonValue c && c.TryGetValue<string>(out var command) && command.Contains(GeminiCommand, StringComparison.Ordinal);

    // Writes only a changed file, after copying the old one aside.
    private static void Save(string file, JsonObject root, bool existed, string before)
    {
        var after = root.ToJsonString(Pretty);
        if (after == before)
        {
            return;
        }

        if (existed)
        {
            // The first backup is the user's pristine file: never replaced by a later one.
            if (!File.Exists(file + BackupSuffix))
            {
                File.Copy(file, file + BackupSuffix);
            }
        }

        var temp = file + ".edgepad-tmp";
        PrivateStorage.CreateDirectory(Path.GetDirectoryName(file)!);
        try
        {
            File.WriteAllText(temp, after + "\n", new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp, existed ? File.GetUnixFileMode(file) : UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            File.Delete(temp);
            throw;
        }
    }

    private void ConnectOpenCode()
    {
        var text = Encoding.UTF8.GetString(Template(OpenCodeTemplate));
        var file = OpenCodeFile;
        if (File.Exists(file))
        {
            if (!IsOurPlugin(file))
            {
                throw new InvalidDataException("An OpenCode plugin of that name is not ours");
            }

            if (File.ReadAllText(file) == text)
            {
                return;
            }
        }

        PrivateStorage.Replace(file, temp => File.WriteAllText(temp, text, new UTF8Encoding(false)));
    }

    private void DisconnectOpenCode()
    {
        if (File.Exists(OpenCodeFile) && IsOurPlugin(OpenCodeFile))
        {
            File.Delete(OpenCodeFile);
        }
    }

    private static bool IsOurPlugin(string file)
    {
        using var reader = new StreamReader(file, Encoding.UTF8);
        return (reader.ReadLine() ?? string.Empty).Contains(OpenCodeMarker, StringComparison.Ordinal);
    }

    private string Fill(string text) =>
        text.Replace(PluginStamp.App, JsonEncodedText.Encode(appPath).ToString(), StringComparison.Ordinal)
            .Replace(PluginStamp.Version, JsonEncodedText.Encode(version).ToString(), StringComparison.Ordinal);

    // The template with __APP__ and __VERSION__ filled; the bytes as UTF-8.
    private byte[] Template(string entry)
    {
        if (!bundle.Entries().Any(e => e.Replace('\\', '/') == entry))
        {
            throw new InvalidDataException("The plugin bundle has no " + entry);
        }

        using var stream = bundle.Open(entry);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        if (copy.Length > MaxBytes)
        {
            throw new InvalidDataException("A plugin file is over 1 MiB");
        }

        return Encoding.UTF8.GetBytes(Fill(Encoding.UTF8.GetString(copy.ToArray())));
    }
}
