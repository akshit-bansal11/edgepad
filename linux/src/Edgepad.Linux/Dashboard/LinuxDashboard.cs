using System.Text;
using Edgepad.Blob;
using Edgepad.Hooks;
using Edgepad.Platform;
using Edgepad.Protocol;
using Edgepad.Ui;

namespace Edgepad.Dashboard;

/// <summary>
/// The Linux app's process-wide dashboard: the integration switches, the plugin installer, and the pieces each integration needs,
/// made only while it is on and let go when it goes off (the boards, the approval broker, hook.json and the hook socket for Claude
/// Code and Codex; the Codex reader; the weather and GitHub clients). One <see cref="DashboardLink"/> per phone session draws on
/// them. With every integration off nothing is made: no socket, no hook.json, no timer, no thread, and FEATURES is unchanged.
/// </summary>
internal sealed class LinuxDashboard : IDisposable
{
    // Long enough for the clipboard poll (one tick a second) to see the copy while it is quiet, so it is never sent to the phone.
    private static readonly TimeSpan QuietCopy = TimeSpan.FromMilliseconds(1500);
    private static readonly string[] AgentKeys = [IntegrationSettings.Claude, IntegrationSettings.Codex];

    private readonly Lock gate = new();
    private readonly Lock applyGate = new();
    private readonly Lock ensureGate = new();
    private readonly string folder;
    private readonly PluginInstaller installer;
    private readonly Lazy<LinuxSecretStore> secrets = new(() => new LinuxSecretStore());
    private readonly Lazy<GitHubSignIn> signIn;
    private readonly Lazy<UsageStore> usage;

    // What EnsureCurrent last said about each agent (re-stamped, Node gone), until the next Connect or Disconnect. Under gate.
    private readonly Dictionary<string, PluginState> notices = [];

    // Under gate.
    private Agents? agents;
    private CodexWatcher? codex;
    private ClaudeSpendWatcher? claude;
    private WeatherClient? weather;
    private GitHubApi? github;
    private LinuxStats? stats;
    private DashboardParts parts = new();
    private bool disposed;

    private int phones;

    // UI thread only.
    private IntegrationsWindow? window;

    /// <param name="folder">Where integrations.txt, dashboard-usage.json and the Codex cache live.</param>
    /// <param name="version">This app's version, stamped into the plugin copies.</param>
    public LinuxDashboard(string folder, string version)
    {
        this.folder = folder;
        Settings = new IntegrationSettings(Path.Combine(folder, IntegrationSettings.FileName));
        installer = new PluginInstaller(new ResourcePluginSource(typeof(LinuxDashboard).Assembly), Settings, PluginInstaller.DefaultRoot(), version);
        signIn = new(() => new GitHubSignIn(secrets.Value));
        usage = new(() => new UsageStore(Path.Combine(folder, UsageStore.FileName), DateTimeOffset.UtcNow));
    }

    /// <summary>The one the app runs with, set by Program before the server starts; null in tests and before.</summary>
    public static LinuxDashboard? Shared { get; set; }

    public IntegrationSettings Settings { get; }

    /// <summary>The pieces changed: each link builds its host again on <see cref="Parts"/>. Raised off the UI thread.</summary>
    public event Action? Changed;

    public DashboardParts Parts
    {
        get
        {
            lock (gate)
            {
                return parts;
            }
        }
    }

    /// <summary>At start: makes what the saved switches ask for, and re-stamps a connected agent's copy when the app or Node moved.</summary>
    public void Start()
    {
        Apply();
        if (AgentKeys.Any(Settings.Flag))
        {
            _ = Task.Run(EnsureCurrent);
        }
    }

    /// <summary>Makes the pieces the switches now ask for and lets go of the rest, then tells every link and phone.</summary>
    public void Apply()
    {
        lock (applyGate)
        {
            CodexWatcher? oldCodex = null;
            ClaudeSpendWatcher? oldClaude = null;
            Agents? oldAgents = null;
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                var agentsOn = AgentKeys.Any(Settings.Flag);
                if (agentsOn && agents is null)
                {
                    agents = OpenAgents();
                }
                else if (!agentsOn)
                {
                    oldAgents = agents;
                    agents = null;
                }

                if (Settings.Flag(IntegrationSettings.Codex) && agents is { } a)
                {
                    codex ??= new CodexWatcher(
                        () => Settings.Flag(IntegrationSettings.Codex),
                        PhoneLinked,
                        a.Sessions,
                        a.Limits,
                        a.Spend,
                        () => DateTimeOffset.UtcNow,
                        CodexWatcher.DefaultRoot(),
                        Path.Combine(folder, CodexWatcher.CacheName));
                }
                else
                {
                    oldCodex = codex;
                    codex = null;
                }

                if (Settings.Flag(IntegrationSettings.Claude) && agents is { } c)
                {
                    claude ??= new ClaudeSpendWatcher(
                        () => Settings.Flag(IntegrationSettings.Claude),
                        PhoneLinked,
                        c.Spend,
                        () => DateTimeOffset.UtcNow,
                        ClaudeSpendWatcher.DefaultRoot(),
                        Path.Combine(folder, ClaudeSpendWatcher.CacheName));
                }
                else
                {
                    oldClaude = claude;
                    claude = null;
                }

                weather = Settings.Flag(IntegrationSettings.Weather) ? weather ?? new WeatherClient() : null;
                github = Settings.Flag(IntegrationSettings.GitHub) ? github ?? new GitHubApi(secrets.Value) : null;

                // Kept until exit once made: a reporter of a host being replaced may still be reading it.
                if (Settings.Flag(IntegrationSettings.Stats))
                {
                    stats ??= new LinuxStats();
                }

                parts = new DashboardParts
                {
                    Sessions = agents?.Sessions,
                    Limits = agents?.Limits,
                    Spend = agents?.Spend,
                    Broker = agents?.Broker,
                    Codex = codex,
                    Claude = claude,
                    Weather = weather,
                    GitHub = github,
                    GitHubSignedIn = github is null ? null : () => signIn.Value.IsSignedIn,
                    AgentHeard = agent => usage.Value.LastHeard(agent) >= 0,
                };
            }

            // Every host moves to the new pieces before the old ones close under it.
            Changed?.Invoke();
            Annotator.FeaturesChanged();
            oldCodex?.Dispose();
            oldClaude?.Dispose();
            oldAgents?.Dispose();
        }
    }

    /// <summary>A new host for one phone link on the current pieces.</summary>
    public IntegrationHost NewHost(Action<Frame> send, BlobSender blobs)
    {
        lock (gate)
        {
            return new IntegrationHost(send, blobs, Settings, parts, stats);
        }
    }

    /// <summary>TEXT 24 entries for agents switched on that cannot work here (no XDG_RUNTIME_DIR, or the hook socket failed: approvals and live sessions; spend and limits still read the files).</summary>
    public string[] Unsupported() =>
        Parts.Broker is not null ? [] : [.. AgentKeys.Where(Settings.Flag).Select(agent => $"{agent}:{UnavailableReason.Unsupported}")];

    /// <summary>The phone's PERM_ANSWER. False (counted as dropped) when no ask waits for it.</summary>
    public bool Answer(string wire) => Parts.Broker?.Answer(wire) ?? false;

    public void PhoneUp() => Interlocked.Increment(ref phones);

    /// <summary>Call before disposing that phone's host, so its Dispose sees the phone gone and defers what waited on it.</summary>
    public void PhoneDown() => Interlocked.Decrement(ref phones);

    /// <summary>Opens the Integrations window, or shows the open one again. UI thread.</summary>
    public void ShowWindow()
    {
        if (window is { } open)
        {
            open.Show();
            return;
        }

        var made = new IntegrationsWindow(Settings, signIn.Value, secrets.Value, new IntegrationsHooks
        {
            Agent = AgentStateOf,
            Connect = ConnectAsync,
            Switched = OnSwitched,
            Copy = CopyQuietly,
            Opened = EnsureCurrent,
        });
        window = made;
        _ = made.Closed.ContinueWith(_ => OnUi(() => window = ReferenceEquals(window, made) ? null : window), TaskScheduler.Default);
        made.Show();
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            codex?.Dispose();
            claude?.Dispose();
            agents?.Dispose();
            stats?.Dispose();
            codex = null;
            claude = null;
            agents = null;
        }

        if (secrets.IsValueCreated)
        {
            secrets.Value.Dispose();
        }
    }

    private bool PhoneLinked() => Volatile.Read(ref phones) > 0;

    // The shared boards always (the Codex and Claude file readers feed them); the hook socket, hook.json and the broker only with a
    // private runtime folder. Without one, approvals and live sessions are unsupported while spend and limits still read the files.
    private Agents? OpenAgents()
    {
        var store = usage.Value;
        var sessions = new SessionBoard(() => DateTimeOffset.UtcNow);
        var limits = new LimitsBoard(store);
        var spend = new SpendBoard(store);
        var dir = HookEndpoint.SocketDirectory();
        if (!OperatingSystem.IsLinux() || LinuxHookListener.Open(dir) is not { } listener)
        {
            return new Agents(sessions, limits, spend, null, null, null);
        }

        HookEndpoint endpoint;
        try
        {
            endpoint = new HookEndpoint(Path.Combine(dir!, HookEndpoint.FileName), listener.Endpoint);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            listener.Dispose();
            Log.Write($"hook.json could not be written, so approvals and live sessions are not offered: {e.GetType().Name}");
            return new Agents(sessions, limits, spend, null, null, null);
        }

        var broker = new PermissionBroker(sessions, PhoneLinked);
        var server = new HookServer(listener, () => endpoint.Token, broker, sessions, limits, spend, store, Settings);
        // An agent now works with Edgepad: the phone names it at once and the window shows it.
        server.AgentReady += _ => OnUi(() =>
        {
            Annotator.FeaturesChanged();
            window?.Refresh();
        });
        server.Start();
        return new Agents(sessions, limits, spend, broker, endpoint, server);
    }

    // An integration was switched on or off in the window (UI thread). Off it, so a slow phone write never holds the UI.
    private void OnSwitched(string key, bool on) => _ = Task.Run(() =>
    {
        try
        {
            Apply();
            if (on)
            {
                // A new token whenever something is switched on (a fresh endpoint already has one; another costs nothing).
                Agents? current;
                lock (gate)
                {
                    current = agents;
                }

                current?.Endpoint?.Rotate();
            }
        }
        catch (Exception e)
        {
            // Nothing may escape a pool thread.
            Log.Write($"Turning {key} {(on ? "on" : "off")} failed: {e.GetType().Name}: {e.Message}");
        }
    });

    // Off the UI thread: re-stamps a connected agent's copy when the app or Node moved, and remembers what it said for the window.
    private void EnsureCurrent()
    {
        lock (ensureGate)
        {
            try
            {
                var found = installer.EnsureCurrentAsync(CancellationToken.None).GetAwaiter().GetResult();
                lock (gate)
                {
                    foreach (var (agent, state) in found)
                    {
                        notices[agent] = state;
                    }
                }

                foreach (var (agent, state) in found)
                {
                    Log.Write($"The {agent} plugin copy: {state}");

                    // Codex skips changed hooks without a word until they are trusted again: said once, per re-stamp.
                    if (agent == AgentToken.Codex && state == PluginState.AwaitingTrust)
                    {
                        Notifier.Send(LinuxStrings.Notice_CodexTrustTitle, LinuxStrings.Notice_CodexTrust);
                    }
                }
            }
            catch (Exception e)
            {
                // Called on pool threads; nothing may escape one. The copy stays as it was.
                Log.Write($"The plugin copies could not be checked: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    private AgentState AgentStateOf(string agent)
    {
        PluginState? notice;
        lock (gate)
        {
            notice = notices.TryGetValue(agent, out var n) ? n : null;
        }

        var status = installer.Status(agent);
        var restart = notice == PluginState.RestartSessions;
        return status switch
        {
            PluginState.Connected => new AgentState(AgentStatus.Connected, LastHeard(agent), restart),
            PluginState.AwaitingTrust => new AgentState(AgentStatus.AwaitingTrust, Restart: restart),
            _ => new AgentState(AgentStatus.NotConnected),
        };
    }

    private async Task<AgentState> ConnectAsync(string agent, bool on)
    {
        var result = await Task.Run(() => on ? installer.ConnectAsync(agent, CancellationToken.None) : installer.DisconnectAsync(agent, CancellationToken.None));
        lock (gate)
        {
            notices.Remove(agent);
        }

        // A reconnect waits for a real hook again before the phone names the agent.
        if (!on)
        {
            usage.Value.Forget(agent);
        }

        return StateFor(agent, result, LastHeard(agent));
    }

    /// <summary>The window's view of an installer outcome.</summary>
    internal static AgentState StateFor(string agent, PluginResult result, DateTimeOffset? lastHeard = null) => result.State switch
    {
        PluginState.Connected => new AgentState(AgentStatus.Connected, lastHeard, Restart: agent == AgentToken.Claude),
        PluginState.AwaitingTrust => new AgentState(AgentStatus.AwaitingTrust),
        PluginState.RestartSessions => new AgentState(AgentStatus.Connected, lastHeard, Restart: true),
        PluginState.CliNotFound => new AgentState(AgentStatus.NotFound, Commands: Lines(result)),
        PluginState.UpdateCodex => new AgentState(AgentStatus.UpdateCodex),
        PluginState.ManualSteps => new AgentState(AgentStatus.NotConnected, Commands: Lines(result)),
        _ => new AgentState(AgentStatus.NotConnected),
    };

    private static string? Lines(PluginResult result) => result.Commands.Count == 0 ? null : string.Join('\n', result.Commands);

    private DateTimeOffset? LastHeard(string agent)
    {
        var minutes = usage.Value.LastHeard(agent);
        return minutes >= 0 ? DateTimeOffset.FromUnixTimeSeconds(minutes * 60) : null;
    }

    // The commands for the Copy box, kept quiet so the clipboard share does not send them to the phone.
    private static void CopyQuietly(string text) => _ = Task.Run(async () =>
    {
        try
        {
            using var quiet = ClipboardQuiet.Begin();
            new LinuxClipboard(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"), Environment.GetEnvironmentVariable("DISPLAY"))
                .Write(new ClipboardContent(BlobKind.ClipText, Encoding.UTF8.GetBytes(text)));
            await Task.Delay(QuietCopy);
        }
        catch (Exception e)
        {
            // No clipboard tool or display; nothing may escape a pool thread. The commands stay on screen to copy by hand.
            Log.Write($"The commands could not be copied: {e.GetType().Name}: {e.Message}");
        }
    });

    private static void OnUi(Action work)
    {
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    work();
                }
                catch (Exception e)
                {
                    Log.Write($"Integrations work on the UI thread failed: {e.GetType().Name}: {e.Message}");
                }
            });
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            // No UI loop (quitting, or no display): nothing to show.
        }
    }

    // Claude Code's and Codex's shared pieces; the server first, then hook.json.
    private sealed record Agents(SessionBoard Sessions, LimitsBoard Limits, SpendBoard Spend, PermissionBroker? Broker, HookEndpoint? Endpoint, HookServer? Server) : IDisposable
    {
        public void Dispose()
        {
            Server?.Dispose();
            Endpoint?.Dispose();
        }
    }
}

/// <summary>
/// One phone session's side of the dashboard: its <see cref="IntegrationHost"/>, built again whenever a switch moves, and the
/// FEATURES and UNAVAILABLE texts with the dashboard's tokens added. The phone is counted for as long as the link lives.
/// </summary>
internal sealed class DashboardLink : IDisposable
{
    private readonly LinuxDashboard dashboard;
    private readonly Action<Frame> send;
    private readonly BlobSender blobs;
    private readonly Lock gate = new();
    private IntegrationHost host;
    private int watch;

    // The session's own UNAVAILABLE entries as last annotated, so one the host sends again (GitHub refused or accepted a token) keeps them.
    private string unavailableBase = string.Empty;
    private bool disposed;

    /// <param name="send">Writes a frame to the phone unchanged (not through <see cref="Annotate"/>).</param>
    public DashboardLink(LinuxDashboard dashboard, Action<Frame> send, BlobSender blobs)
    {
        this.dashboard = dashboard;
        this.send = send;
        this.blobs = blobs;
        dashboard.PhoneUp();
        host = dashboard.NewHost(FromHost, blobs);
        dashboard.Changed += Rebuild;
    }

    private IntegrationHost Host
    {
        get
        {
            lock (gate)
            {
                return host;
            }
        }
    }

    /// <summary>The frame with the dashboard's FEATURES and UNAVAILABLE entries added; any other frame as it is.</summary>
    public Frame Annotate(Frame frame)
    {
        switch (frame)
        {
            case Text { Kind: (byte)TextKind.Features } t:
                return t with { Value = Join(t.Value, Host.Features()) };
            case Text { Kind: (byte)TextKind.Unavailable } t:
                lock (gate)
                {
                    unavailableBase = t.Value;
                }

                return t with { Value = Join(t.Value, [.. Host.Unavailable(), .. dashboard.Unsupported()]) };
            default:
                return frame;
        }
    }

    /// <summary>Once FEATURES has gone out.</summary>
    public void Start() => Host.Start();

    /// <summary>The phone's DASH_WATCH.</summary>
    public void OnWatch(int mask)
    {
        IntegrationHost current;
        lock (gate)
        {
            watch = mask;
            current = host;
        }

        current.OnWatch(mask);
    }

    /// <summary>The phone's PERM_ANSWER.</summary>
    public bool Answer(string wire) => dashboard.Answer(wire);

    public void Dispose()
    {
        IntegrationHost current;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            current = host;
        }

        dashboard.Changed -= Rebuild;

        // Not counted first, so the host's Dispose sees this phone gone and sends what waited on it back to the agent.
        dashboard.PhoneDown();
        current.Dispose();
    }

    // The host's own UNAVAILABLE carries only its entries: sent as the whole list, with the session's and the agents' added.
    private void FromHost(Frame frame)
    {
        if (frame is Text { Kind: (byte)TextKind.Unavailable })
        {
            string session;
            lock (gate)
            {
                session = unavailableBase;
            }

            frame = Annotate(new Text((byte)TextKind.Unavailable, session));
        }

        send(frame);
    }

    internal static string Join(string text, IEnumerable<string> more) =>
        string.Join('/', more.Prepend(text).Where(entry => entry.Length > 0));

    private void Rebuild()
    {
        IntegrationHost old;
        IntegrationHost next;
        int mask;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            old = host;
            next = host = dashboard.NewHost(FromHost, blobs);
            mask = watch;
        }

        old.Dispose();
        next.Start();
        next.OnWatch(mask);
    }
}
