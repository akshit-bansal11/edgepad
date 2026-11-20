using System.Globalization;
using System.Runtime.InteropServices;
using Edgepad.Blob;
using Edgepad.Hooks;
using Edgepad.Protocol;
using Edgepad.Ui;

namespace Edgepad.Dashboard;

/// <summary>
/// The tray's process-wide side of the dashboard: the settings, the secret store and GitHub sign-in, the plugin installer, and the
/// pieces the integrations need while they are on. With every switch off (the default) nothing is made but those first four, which
/// hold no thread, handle or timer, and <see cref="Attach"/> gives a phone a link that holds nothing, so it still hears the
/// FEATURES a later switch adds.
/// <para>
/// Claude or Codex on: the session, limits and spend boards and the permission broker (plain objects, kept once made), and the hook
/// channel (the user-only pipe, hook.json and HookServer's accept loop), closed and hook.json deleted when both are off. Codex on:
/// the Codex watcher, whose timer runs only while Codex is on and a phone is attached (<see cref="CodexWatcher.Evaluate"/>); it is
/// kept once made rather than disposed, because hosts made earlier still hold it and would otherwise call into a disposed one.
/// Every switch turned on makes a new hook token.
/// </para>
/// </summary>
internal sealed partial class WindowsIntegrations : IDisposable
{
    private static readonly string[] FlagKeys =
    [
        IntegrationSettings.Stats, IntegrationSettings.Weather, IntegrationSettings.Claude, IntegrationSettings.Codex, IntegrationSettings.GitHub,
    ];

    private readonly string folder;
    private readonly PluginInstaller installer;
    private readonly Lock gate = new();

    // What Connect, Disconnect or a re-stamp last said per agent, for the parts of a row the saved switches cannot tell. Under gate.
    private readonly Dictionary<string, AgentStatus> last = [];

    // Under gate. Every live phone link, so an agent switch can move them to new hosts.
    private readonly List<DashboardLink> links = [];

    // Under gate. Made when first needed and kept: none holds a thread or a handle.
    private UsageStore? usage;
    private SessionBoard? sessions;
    private LimitsBoard? limits;
    private SpendBoard? spend;
    private PermissionBroker? broker;
    private CodexWatcher? codex;
    private ClaudeSpendWatcher? claude;
    private WeatherClient? weather;
    private GitHubApi? github;

    // Under gate. Open only while Claude or Codex is on.
    private HookEndpoint? endpoint;
    private HookServer? server;

    private int phones;
    private bool disposed;

    /// <param name="folder">The app's data folder (the PIN store's).</param>
    /// <param name="version">This app's version, stamped into the plugin copy.</param>
    public WindowsIntegrations(string folder, string version)
    {
        this.folder = folder;
        Settings = new IntegrationSettings(Path.Combine(folder, IntegrationSettings.FileName));
        Secrets = new WindowsSecretStore();
        SignIn = new GitHubSignIn(Secrets);
        installer = new PluginInstaller(new ResourcePluginSource(typeof(WindowsIntegrations).Assembly), Settings, PluginInstaller.DefaultRoot(), version, shortPath: ShortPath);
    }

    /// <summary>An agent started working with Edgepad (its first hook since Connect, or Codex trusted). Raised on a hook thread.</summary>
    public event Action? Trusted;

    public IntegrationSettings Settings { get; }

    public WindowsSecretStore Secrets { get; }

    public GitHubSignIn SignIn { get; }

    private bool AgentsOn => Settings.Active(IntegrationSettings.Claude) || Settings.Active(IntegrationSettings.Codex);

    private bool PhoneLinked() => Volatile.Read(ref phones) > 0;

    /// <summary>At app start: opens what the saved switches ask for. Nothing when they are all off.</summary>
    public void Start()
    {
        lock (gate)
        {
            Apply(rotate: false);
        }
    }

    /// <summary>Raised after <see cref="SetDeveloper"/>, on the calling thread.</summary>
    public event Action? DeveloperChanged;

    /// <summary>Turns developer mode on or off (the tray or the phone): keeps it, applies it and tells every linked phone.</summary>
    public void SetDeveloper(bool on)
    {
        _ = Settings.SetFlag(IntegrationSettings.Developer, on);
        Changed(IntegrationSettings.Developer);
        DeveloperChanged?.Invoke();
    }

    /// <summary>The Integrations window changed <paramref name="key"/>: opens or closes what it governs; a switch turned on rotates the token.</summary>
    public void Changed(string key)
    {
        CodexWatcher? watcher;
        List<DashboardLink> live;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (key == IntegrationSettings.City)
            {
                // The saved place was the old city's; a fresh client looks the new one up.
                _ = Settings.Set(IntegrationSettings.Lat, string.Empty);
                _ = Settings.Set(IntegrationSettings.Lon, string.Empty);
                _ = Settings.Set(IntegrationSettings.Place, string.Empty);
                weather = null;
            }

            Apply(rotate: FlagKeys.Contains(key) && Settings.Flag(key));
            watcher = codex;
            live = [.. links];
        }

        var agent = key is IntegrationSettings.Claude or IntegrationSettings.Codex or IntegrationSettings.Developer;
        foreach (var link in live)
        {
            if (agent)
            {
                Rebuild(link);
            }

            // A switch or a GitHub sign-in reaches a linked phone now, not at its next connection.
            link.FeaturesChanged?.Invoke();
        }

        watcher?.Evaluate();
    }

    /// <summary>
    /// One phone's dashboard (a link that holds nothing while every switch is off). The phone counts as linked for approvals only while its host has
    /// the broker (an agent on, the hook channel open), so no ask waits on a phone that cannot see it; turning an agent on or
    /// off moves every live link to a new host. Disposing stops counting it before the host goes, so the host's last word to the
    /// broker sees the phone gone.
    /// </summary>
    /// <param name="send">Writes a frame to this phone; must not throw for a dead link.</param>
    public DashboardLink? Attach(Action<Frame> send, BlobSender blobs)
    {
        lock (gate)
        {
            if (disposed)
            {
                return null;
            }

            var stats = Settings.Flag(IntegrationSettings.Stats) ? new WindowsStats() : null;
            var parts = Parts();
            if (parts.Broker is not null)
            {
                Interlocked.Increment(ref phones);
            }

            var link = new DashboardLink(this, send, blobs, stats, new IntegrationHost(send, blobs, Settings, parts, stats), parts.Broker);
            links.Add(link);
            return link;
        }
    }

    /// <summary>The row's state: the saved switch, refined by what the last Connect, Disconnect or re-stamp reported.</summary>
    public AgentStatus Status(string agent)
    {
        var now = Map(PluginResult.Of(installer.Status(agent)));
        lock (gate)
        {
            if (!last.TryGetValue(agent, out var said))
            {
                return now;
            }

            return now.State switch
            {
                AgentState.NotConnected when said.State is not (AgentState.Connected or AgentState.AwaitingTrust) => said,
                AgentState.Connected => now with { Restart = said.Restart },
                _ => now,
            };
        }
    }

    public async Task<AgentStatus> ConnectAsync(string agent)
    {
        var status = Map(await installer.ConnectAsync(agent, CancellationToken.None).ConfigureAwait(false));

        // Claude Code reads plugins when a session starts: the row says to restart open ones.
        if (agent == AgentToken.Claude && status.State == AgentState.Connected)
        {
            status = status with { Restart = true };
        }

        Remember(agent, status);
        return status;
    }

    public async Task<AgentStatus> DisconnectAsync(string agent)
    {
        var status = Map(await installer.DisconnectAsync(agent, CancellationToken.None).ConfigureAwait(false));

        // A reconnect waits for a real hook again before the phone names the agent.
        UsageStore? store;
        lock (gate)
        {
            store = usage;
        }

        store?.Forget(agent);
        Remember(agent, status);
        return status;
    }

    /// <summary>
    /// Re-stamps a connected plugin an update or a moved app left behind. True when Codex must trust Edgepad's hooks again.
    /// Never throws; runs no program while both agents are off.
    /// </summary>
    public async Task<bool> EnsureCurrentAsync()
    {
        var retrust = false;
        try
        {
            foreach (var (agent, state) in await installer.EnsureCurrentAsync(CancellationToken.None).ConfigureAwait(false))
            {
                Remember(agent, Map(PluginResult.Of(state)));
                retrust |= agent == AgentToken.Codex && state == PluginState.AwaitingTrust;
            }
        }
        catch (Exception e)
        {
            // A task of its own: nothing may escape it. The row keeps what it had.
            Log.Write($"The Edgepad plugin could not be brought up to date: {e.GetType().Name}");
        }

        return retrust;
    }

    /// <summary>UTC minutes of the agent's last hook message, or -1.</summary>
    public long LastHeard(string agent)
    {
        lock (gate)
        {
            return usage?.LastHeard(agent) ?? -1;
        }
    }

    /// <summary>The signed-in GitHub login, for the window only.</summary>
    public Task<string?> LoginAsync(CancellationToken cancellationToken) => new GitHubApi(Secrets).LoginAsync(cancellationToken);

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StopHooks();
            codex?.Dispose();
            claude?.Dispose();
        }

        SavePlace();
    }

    internal static AgentStatus Map(PluginResult result) => result.State switch
    {
        PluginState.Connected => new AgentStatus(AgentState.Connected),
        PluginState.RestartSessions => new AgentStatus(AgentState.Connected, Restart: true),
        PluginState.AwaitingTrust => new AgentStatus(AgentState.AwaitingTrust),
        PluginState.ManualSteps => new AgentStatus(AgentState.ManualSteps, string.Join(Environment.NewLine, result.Commands)),
        PluginState.CliNotFound => new AgentStatus(AgentState.NotFound, string.Join(Environment.NewLine, result.Commands)),
        PluginState.UpdateCodex => new AgentStatus(AgentState.UpdateCodex),

        // Failed was logged where it happened; the row offers Connect again.
        _ => new AgentStatus(AgentState.NotConnected),
    };

    /// <summary>The 8.3 short form of an existing path, or null when Windows has none for it (8.3 names off on that volume).</summary>
    internal static string? ShortPath(string path)
    {
        var buffer = new char[1024];
        var length = GetShortPathNameW(path, buffer, (uint)buffer.Length);
        return length is > 0 and < 1024 ? new string(buffer, 0, (int)length) : null;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetShortPathNameW(string longPath, [Out] char[] shortPath, uint length);

    private void Remember(string agent, AgentStatus status)
    {
        lock (gate)
        {
            if (status.State == AgentState.NotConnected)
            {
                _ = last.Remove(agent);
            }
            else
            {
                last[agent] = status;
            }
        }
    }

    // Under gate.
    private void Apply(bool rotate)
    {
        if (disposed)
        {
            return;
        }

        if (AgentsOn)
        {
            usage ??= new UsageStore(Path.Combine(folder, UsageStore.FileName), DateTimeOffset.UtcNow);
            sessions ??= new SessionBoard(() => DateTimeOffset.UtcNow);
            limits ??= new LimitsBoard(usage);
            spend ??= new SpendBoard(usage);
            broker ??= new PermissionBroker(sessions, PhoneLinked);
            if (server is null)
            {
                StartHooks();
            }
            else if (rotate)
            {
                _ = endpoint?.Rotate();
            }
        }
        else
        {
            StopHooks();
        }

        if (Settings.Active(IntegrationSettings.Codex) && sessions is not null && limits is not null && spend is not null)
        {
            codex ??= new CodexWatcher(
                () => Settings.Active(IntegrationSettings.Codex),
                PhoneLinked,
                sessions,
                limits,
                spend,
                () => DateTimeOffset.UtcNow,
                CodexWatcher.DefaultRoot(),
                Path.Combine(folder, CodexWatcher.CacheName));
        }

        if (Settings.Active(IntegrationSettings.Claude) && spend is not null)
        {
            claude ??= new ClaudeSpendWatcher(
                () => Settings.Active(IntegrationSettings.Claude),
                PhoneLinked,
                spend,
                () => DateTimeOffset.UtcNow,
                ClaudeSpendWatcher.DefaultRoot(),
                Path.Combine(folder, ClaudeSpendWatcher.CacheName));
        }

        if (Settings.Flag(IntegrationSettings.Weather))
        {
            weather ??= SavedWeather();
        }

        // Dropped when off, so a sign-in to another account starts from empty lists.
        github = Settings.Active(IntegrationSettings.GitHub) ? github ?? new GitHubApi(Secrets) : null;
    }

    // Under gate. The pieces a host gets now; the broker only while an agent is on and its channel is open.
    private DashboardParts Parts()
    {
        var agents = AgentsOn && broker is not null && server is not null;
        var store = usage;
        return new DashboardParts
        {
            Sessions = agents ? sessions : null,
            Limits = agents ? limits : null,
            Spend = agents ? spend : null,
            Broker = agents ? broker : null,
            Codex = codex,
            Claude = claude,
            Weather = Settings.Flag(IntegrationSettings.Weather) ? weather : null,
            GitHub = Settings.Active(IntegrationSettings.GitHub) ? github : null,
            GitHubSignedIn = () => SignIn.IsSignedIn,
            AgentHeard = agent => store?.LastHeard(agent) >= 0,
        };
    }

    // A hook thread: an agent now works with Edgepad, so every linked phone hears its FEATURES again and the window refreshes.
    private void Ready()
    {
        List<DashboardLink> live;
        lock (gate)
        {
            live = [.. links];
        }

        foreach (var link in live)
        {
            link.FeaturesChanged?.Invoke();
        }

        Trusted?.Invoke();
    }

    // A live link moves to a host on the current pieces, so an agent switched on while a phone is linked reaches it (PERM_ASK
    // and PERM_ANSWER) and one switched off stops counting it. The phone stops being counted before its old host goes.
    private void Rebuild(DashboardLink link)
    {
        IntegrationHost old;
        IntegrationHost next;
        int mask;
        lock (gate)
        {
            if (disposed || !links.Contains(link))
            {
                return;
            }

            var parts = Parts();
            (old, mask) = link.Swap(new IntegrationHost(link.Send, link.Blobs, Settings, parts, link.Stats), parts.Broker, out var hadBroker);
            next = link.Host;
            if (parts.Broker is not null && !hadBroker)
            {
                Interlocked.Increment(ref phones);
            }
            else if (parts.Broker is null && hadBroker)
            {
                Interlocked.Decrement(ref phones);
            }
        }

        old.Dispose();
        next.Start();
        next.OnWatch(mask);
    }

    // Under gate. The pipe first, then hook.json naming it, then the accept loop. A pipe that cannot be made leaves the agents
    // on their own prompts (no hook.json, so the hook client finds nothing) and is logged; turning a switch on again retries.
    private void StartHooks()
    {
        if (HookEndpoint.DefaultFile() is not { } file || broker is null || sessions is null || limits is null || spend is null || usage is null)
        {
            return;
        }

        WindowsHookListener? listener = null;
        HookEndpoint? made = null;
        try
        {
            listener = new WindowsHookListener();
            var current = new HookEndpoint(file, listener.Endpoint);
            made = current;
            var hooks = new HookServer(listener, () => current.Token, broker, sessions, limits, spend, usage, Settings);
            hooks.AgentReady += _ => Ready();
            hooks.Start();
            endpoint = made;
            server = hooks;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The hook channel could not open, so approvals stay in the terminal: {e.GetType().Name}");
            made?.Dispose();
            listener?.Dispose();
        }
    }

    // Under gate. Closing the server ends every waiting ask (the agent shows its own prompt); the file goes with it.
    private void StopHooks()
    {
        server?.Dispose();
        server = null;
        endpoint?.Dispose();
        endpoint = null;
    }

    // A client that already knows where the saved city is, so only the forecast is asked.
    private WeatherClient SavedWeather()
    {
        var client = new WeatherClient();
        if (double.TryParse(Settings.Text(IntegrationSettings.Lat), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            && double.TryParse(Settings.Text(IntegrationSettings.Lon), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            client.Remember(Settings.Text(IntegrationSettings.City), new WeatherPlace(lat, lon, Settings.Text(IntegrationSettings.Place)));
        }

        return client;
    }

    // Saves the place the last lookup found, once, so the next start skips the city lookup. ponytail: written when a phone
    // leaves or the app quits, not after each lookup; a crash only costs one more lookup.
    private void SavePlace()
    {
        WeatherPlace? place;
        lock (gate)
        {
            place = weather?.Place;
            if (place is null || Settings.Text(IntegrationSettings.Lat).Length > 0)
            {
                return;
            }
        }

        _ = Settings.Set(IntegrationSettings.Lat, place.Lat.ToString("R", CultureInfo.InvariantCulture));
        _ = Settings.Set(IntegrationSettings.Lon, place.Lon.ToString("R", CultureInfo.InvariantCulture));
        _ = Settings.Set(IntegrationSettings.Place, place.Name);
    }

    private void Detach(DashboardLink link)
    {
        IntegrationHost host;
        lock (gate)
        {
            _ = links.Remove(link);
            host = link.Host;

            // Not counted first, so the host's goodbye to the broker and the Codex watcher sees this phone gone. A phone whose
            // host had no broker was never counted.
            if (link.Broker is not null)
            {
                Interlocked.Decrement(ref phones);
            }
        }

        try
        {
            host.Dispose();
        }
        finally
        {
            link.Stats?.Dispose();
            SavePlace();
        }
    }

    /// <summary>
    /// One phone's dashboard: its current host (built again when an agent switch moves) and, while Claude or Codex is on, the
    /// broker its PERM_ANSWERs go to. Host and Broker change only under the owner's gate.
    /// </summary>
    internal sealed class DashboardLink(WindowsIntegrations owner, Action<Frame> send, BlobSender blobs, WindowsStats? stats, IntegrationHost host, PermissionBroker? broker) : IDisposable
    {
        private int disposed;
        private int watch;

        public IntegrationHost Host { get; private set; } = host;

        public PermissionBroker? Broker { get; private set; } = broker;

        public WindowsStats? Stats { get; } = stats;

        public Action<Frame> Send { get; } = send;

        public BlobSender Blobs { get; } = blobs;

        /// <summary>Sends this phone its FEATURES again; set by the session that made the link.</summary>
        public Action? FeaturesChanged { get; set; }

        /// <summary>The phone's DASH_WATCH, kept so a rebuilt host starts where this one was.</summary>
        public void OnWatch(int mask)
        {
            Volatile.Write(ref watch, mask);
            Host.OnWatch(mask);
        }

        /// <summary>The phone's PERM_ANSWER, to whichever broker this link has now. False when none.</summary>
        public bool Answer(string wire) => Broker?.Answer(wire) ?? false;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.Detach(this);
            }
        }

        // Under the owner's gate: the new host and broker in, the old host (and its watch mask) out.
        internal (IntegrationHost Old, int Mask) Swap(IntegrationHost next, PermissionBroker? nextBroker, out bool hadBroker)
        {
            var old = Host;
            hadBroker = Broker is not null;
            Host = next;
            Broker = nextBroker;
            return (old, Volatile.Read(ref watch));
        }
    }
}
