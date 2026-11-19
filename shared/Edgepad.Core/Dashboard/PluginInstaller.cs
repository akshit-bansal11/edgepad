using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Edgepad.Protocol;
using Edgepad.Trust;

namespace Edgepad.Dashboard;

/// <summary>Where an agent's integration stands, for the Integrations window.</summary>
internal enum PluginState
{
    /// <summary>Not connected.</summary>
    NotConnected,

    /// <summary>Installed and (Codex) trusted.</summary>
    Connected,

    /// <summary>Codex installed the plugin; the person still has to trust its hooks in /hooks or Settings > Hooks.</summary>
    AwaitingTrust,

    /// <summary>The copy was re-stamped (app updated or moved): open sessions need a restart or /reload-plugins.</summary>
    RestartSessions,

    /// <summary>The CLI could not be run, or did not report success: <see cref="PluginResult.Commands"/> holds what to run by hand.</summary>
    ManualSteps,

    /// <summary>Never returned since 5.0.0 (the hooks run the app, not Node). Kept only so the platforms' state maps still build; drop it with them.</summary>
    NodeMissing,

    /// <summary>The agent's CLI is not on PATH. <see cref="PluginResult.Commands"/> still names the folder, for Codex Desktop's Plugins Directory.</summary>
    CliNotFound,

    /// <summary>Codex is older than 0.159.</summary>
    UpdateCodex,

    /// <summary>The plugin copy could not be written (disk refused, damaged bundle). Logged.</summary>
    Failed,
}

/// <summary>An outcome and, for <see cref="PluginState.ManualSteps"/> and <see cref="PluginState.CliNotFound"/>, the exact lines for the Copy box.</summary>
internal sealed record PluginResult(PluginState State, IReadOnlyList<string> Commands)
{
    public static PluginResult Of(PluginState state) => new(state, []);
}

/// <summary>
/// Connect and Disconnect for the Claude Code and Codex plugins. Connect stamps the plugin copy (<see cref="PluginStamp"/>) under
/// <c>&lt;plugins&gt;/&lt;agent&gt;</c> and then runs the vendor's own non-interactive CLI on it; it never edits a file under
/// ~/.claude or ~/.codex and never bypasses Codex's hook trust. Programs are found only in absolute PATH folders (never the working
/// directory), run by absolute path with fixed words and one variable argument, our own folder, which must be letters, digits,
/// space and <c>_ . : \ / -</c> (so a Windows <c>.cmd</c> shim cannot be made to run something else); anything else, any failure
/// and any output that does not say ok falls back to <see cref="PluginState.ManualSteps"/> with the commands to paste.
/// </summary>
internal sealed partial class PluginInstaller
{
    public const string MarketplaceName = "edgepad";
    public const string PluginId = "edgepad@edgepad";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    public static readonly Version MinCodex = new(0, 159);

    private static readonly string[] Agents = [AgentToken.Claude, AgentToken.Codex];
    private static readonly string[] VersionArgs = ["--version"];

    private readonly IPluginSource bundle;
    private readonly IntegrationSettings settings;
    private readonly string pluginsRoot;
    private readonly string version;
    private readonly string appPath;
    private readonly IProcessRunner runner;
    private readonly Func<string?> path;

    /// <param name="pluginsRoot">The folder holding the per-agent copies; <see cref="DefaultRoot"/> in the apps.</param>
    /// <param name="version">This app's version, stamped as the plugin's and stored to notice an update.</param>
    /// <param name="path">For tests: the PATH value. The process's PATH by default.</param>
    /// <param name="appPath">For tests: this app's own executable, which the hooks run as <c>"__APP__" --hook &lt;agent&gt; &lt;kind&gt;</c>
    /// and which is stored to notice a moved app. <see cref="Environment.ProcessPath"/> by default.</param>
    public PluginInstaller(IPluginSource bundle, IntegrationSettings settings, string pluginsRoot, string version, IProcessRunner? runner = null, Func<string?>? path = null, string? appPath = null)
    {
        this.bundle = bundle;
        this.settings = settings;
        this.pluginsRoot = pluginsRoot;
        this.version = version;
        this.appPath = appPath ?? Environment.ProcessPath ?? string.Empty;
        this.runner = runner ?? new ProcessRunner();
        this.path = path ?? (() => Environment.GetEnvironmentVariable("PATH"));
    }

    /// <summary><c>%LOCALAPPDATA%\Edgepad\plugins</c>, <c>~/Library/Application Support/Edgepad/plugins</c>, or <c>$XDG_DATA_HOME/Edgepad/plugins</c> (<c>~/.local/share</c> when unset).</summary>
    public static string DefaultRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(UserFolder.Current, "plugins");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Application Support", "Edgepad", "plugins");
        }

        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(data is { Length: > 0 } && Path.IsPathFullyQualified(data) ? data : Path.Combine(home, ".local", "share"), "Edgepad", "plugins");
    }

    /// <summary>The marketplace folder of an agent's copy: what <c>plugin marketplace add</c> is pointed at.</summary>
    public string FolderOf(string agent) => Path.Combine(pluginsRoot, agent);

    /// <summary>Connected, AwaitingTrust (Codex not yet trusted) or NotConnected, from the saved switches.</summary>
    public PluginState Status(string agent) =>
        !settings.Flag(agent) ? PluginState.NotConnected
        : agent == AgentToken.Codex && !settings.Flag(IntegrationSettings.CodexTrusted) ? PluginState.AwaitingTrust
        : PluginState.Connected;

    /// <summary>The exact lines for the Copy box: the connect (or disconnect) commands with our folder quoted.</summary>
    public IReadOnlyList<string> Commands(string agent, bool connect = true)
    {
        var cli = agent == AgentToken.Claude ? "claude" : "codex";
        return [.. Arguments(agent, connect, FolderOf(agent)).Select(args => cli + " " + string.Join(' ', args.Select(a => a.Contains(' ', StringComparison.Ordinal) ? $"\"{a}\"" : a)))];
    }

    /// <summary>Stamps the copy and installs it with the vendor CLI. Saves the switch and what was stamped only on success.</summary>
    public async Task<PluginResult> ConnectAsync(string agent, CancellationToken cancellationToken)
    {
        var folder = FolderOf(agent);
        if (!SafePath(folder) || !SafePath(appPath))
        {
            return new PluginResult(PluginState.ManualSteps, Commands(agent));
        }

        if (!Stamp(agent))
        {
            return PluginResult.Of(PluginState.Failed);
        }

        if (FindCli(agent, path()) is not { } cli)
        {
            return new PluginResult(PluginState.CliNotFound, Commands(agent));
        }

        if (agent == AgentToken.Codex)
        {
            var reply = await runner.RunAsync(cli, VersionArgs, folder, Timeout, cancellationToken).ConfigureAwait(false);
            if (reply.ExitCode != 0 || CodexVersion(reply.LastLine) is not { } codex || codex < MinCodex)
            {
                return PluginResult.Of(PluginState.UpdateCodex);
            }
        }

        foreach (var args in Arguments(agent, connect: true, folder))
        {
            if (!Succeeded(await runner.RunAsync(cli, args, folder, Timeout, cancellationToken).ConfigureAwait(false), strict: agent == AgentToken.Claude))
            {
                return new PluginResult(PluginState.ManualSteps, Commands(agent));
            }
        }

        if (agent == AgentToken.Codex)
        {
            settings.SetFlag(IntegrationSettings.CodexTrusted, false);
        }

        settings.Set(IntegrationSettings.PluginVersionKey(agent), version);
        settings.Set(IntegrationSettings.PluginAppKey(agent), appPath);
        settings.SetFlag(agent, true);
        return PluginResult.Of(agent == AgentToken.Codex ? PluginState.AwaitingTrust : PluginState.Connected);
    }

    /// <summary>
    /// Turns the switch off at once (the hook server then defers), uninstalls with the vendor CLI and deletes the copy. When the CLI
    /// cannot do it, the copy stays (its hooks only defer now) and the commands to run by hand come back.
    /// </summary>
    public async Task<PluginResult> DisconnectAsync(string agent, CancellationToken cancellationToken)
    {
        settings.SetFlag(agent, false);
        var folder = FolderOf(agent);
        var manual = new PluginResult(PluginState.ManualSteps, Commands(agent, connect: false));
        if (!SafePath(folder) || FindCli(agent, path()) is not { } cli)
        {
            return manual;
        }

        foreach (var args in Arguments(agent, connect: false, folder))
        {
            if (!Succeeded(await runner.RunAsync(cli, args, Directory.Exists(folder) ? folder : Path.GetDirectoryName(cli)!, Timeout, cancellationToken).ConfigureAwait(false), strict: agent == AgentToken.Claude))
            {
                return manual;
            }
        }

        try
        {
            PluginStamp.Remove(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The {agent} plugin folder could not be removed: {e.GetType().Name}");
        }

        settings.Set(IntegrationSettings.PluginVersionKey(agent), string.Empty);
        settings.Set(IntegrationSettings.PluginAppKey(agent), string.Empty);
        if (agent == AgentToken.Codex)
        {
            settings.SetFlag(IntegrationSettings.CodexTrusted, false);
        }

        return PluginResult.Of(PluginState.NotConnected);
    }

    /// <summary>
    /// At app start and when the window opens: a connected agent whose copy was stamped by another app version or for an app at
    /// another path (including a copy from before 5.0.0, which ran Node) is re-stamped in place, with no CLI call, and reported
    /// <see cref="PluginState.RestartSessions"/>; <see cref="PluginState.Failed"/> when it cannot be. Agents with nothing to do are
    /// not listed.
    /// </summary>
    public Task<IReadOnlyList<(string Agent, PluginState State)>> EnsureCurrentAsync(CancellationToken cancellationToken)
    {
        var notices = new List<(string, PluginState)>();
        foreach (var agent in Agents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!settings.Flag(agent))
            {
                continue;
            }

            if (settings.Text(IntegrationSettings.PluginVersionKey(agent)) == version && settings.Text(IntegrationSettings.PluginAppKey(agent)) == appPath)
            {
                continue;
            }

            if (!SafePath(appPath) || !SafePath(FolderOf(agent)) || !Stamp(agent))
            {
                notices.Add((agent, PluginState.Failed));
                continue;
            }

            settings.Set(IntegrationSettings.PluginVersionKey(agent), version);
            settings.Set(IntegrationSettings.PluginAppKey(agent), appPath);
            notices.Add((agent, PluginState.RestartSessions));
        }

        return Task.FromResult<IReadOnlyList<(string Agent, PluginState State)>>(notices);
    }

    /// <summary>The agent's CLI in the first absolute PATH folder that has it: <c>claude</c> or <c>codex</c>, on Windows as <c>.exe</c> or <c>.cmd</c>.</summary>
    public static string? FindCli(string agent, string? pathVariable)
    {
        var name = agent == AgentToken.Claude ? "claude" : "codex";
        return Candidates(pathVariable, OperatingSystem.IsWindows() ? [name + ".exe", name + ".cmd"] : [name]).FirstOrDefault();
    }

    /// <summary><c>codex-cli 0.159.0</c> to 0.159.0; null for anything else.</summary>
    public static Version? CodexVersion(string text) => Parse(CodexPattern().Match(text.Trim()));

    /// <summary>
    /// Letters, digits, space and <c>_ . : \ / -</c>, plus <c>~</c> for Windows short names such as <c>RUNNER~1</c> (cmd.exe reads it
    /// specially only inside a <c>%…%</c> expansion, and <c>%</c> is refused): nothing a shell or cmd.exe reads specially.
    /// </summary>
    public static bool SafePath(string text) =>
        text.Length > 0 && text.EnumerateRunes().All(r => Rune.IsLetterOrDigit(r) || r.Value is ' ' or '_' or '.' or ':' or '\\' or '/' or '-' or '~');

    /// <summary>Exit 0 and, for Claude (strict), a last line that is JSON with <c>outcome</c> "ok"; for Codex a last line that is JSON whose <c>outcome</c>, if any, is "ok".</summary>
    public static bool Succeeded(ProcessResult result, bool strict)
    {
        if (result.ExitCode != 0 || result.TimedOut)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(result.LastLine);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return doc.RootElement.TryGetProperty("outcome", out var outcome)
                ? outcome.ValueKind == JsonValueKind.String && outcome.GetString() == "ok"
                : !strict;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The argument vectors Connect or Disconnect runs, in order.</summary>
    public static IReadOnlyList<string[]> Arguments(string agent, bool connect, string folder) =>
        (agent, connect) switch
        {
            (AgentToken.Claude, true) => [["plugin", "marketplace", "add", folder, "--json"], ["plugin", "install", PluginId, "--scope", "user", "--json"]],
            (AgentToken.Claude, false) => [["plugin", "uninstall", PluginId, "--scope", "user", "--json"], ["plugin", "marketplace", "remove", MarketplaceName, "--json"]],
            (_, true) => [["plugin", "marketplace", "add", folder, "--json"], ["plugin", "add", PluginId, "--json"]],
            _ => [["plugin", "remove", PluginId, "--json"], ["plugin", "marketplace", "remove", MarketplaceName, "--json"]],
        };

    // Every absolute PATH folder, in order, that holds one of the names as a file; relative entries (and so the working directory) never count.
    private static IEnumerable<string> Candidates(string? pathVariable, string[] names)
    {
        foreach (var folder in (pathVariable ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dir = folder.Trim('"');
            if (!Path.IsPathFullyQualified(dir))
            {
                continue;
            }

            foreach (var name in names)
            {
                var file = Path.Combine(dir, name);
                if (File.Exists(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static Version? Parse(Match match) =>
        match.Success
        && int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
        && int.TryParse(match.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
        && int.TryParse(match.Groups[3].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var patch)
            ? new Version(major, minor, patch)
            : null;

    [GeneratedRegex(@"^codex-cli (\d{1,4})\.(\d{1,4})\.(\d{1,6})(?:[-+][0-9A-Za-z.-]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex CodexPattern();

    private bool Stamp(string agent)
    {
        try
        {
            PluginStamp.Write(bundle, agent, FolderOf(agent), appPath, version);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Write($"The {agent} plugin could not be written: {e.GetType().Name}");
            return false;
        }
    }
}
