using System.Text;
using Edgepad.Blob;
using Edgepad.Hooks;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// The process-wide dashboard pieces a laptop app holds while the matching integration is on; each is null while it is off.
/// One set is shared by every <see cref="IntegrationHost"/>.
/// </summary>
internal sealed class DashboardParts
{
    /// <summary>Claude or Codex on.</summary>
    public SessionBoard? Sessions { get; init; }

    public LimitsBoard? Limits { get; init; }

    public SpendBoard? Spend { get; init; }

    public PermissionBroker? Broker { get; init; }

    /// <summary>Codex on.</summary>
    public CodexWatcher? Codex { get; init; }

    /// <summary>Claude on: reads Claude Code's spend from its transcripts.</summary>
    public ClaudeSpendWatcher? Claude { get; init; }

    /// <summary>Weather on.</summary>
    public WeatherClient? Weather { get; init; }

    /// <summary>GitHub on.</summary>
    public GitHubApi? GitHub { get; init; }

    /// <summary>Whether a GitHub token is saved (<see cref="GitHubSignIn.IsSignedIn"/>).</summary>
    public Func<bool>? GitHubSignedIn { get; init; }
}

/// <summary>
/// One phone link's side of the dashboard; a platform session makes one per link and disposes it when the link ends. Nothing runs
/// until the phone asks: <see cref="OnWatch"/> (DASH_WATCH) starts the stats reporter for bits 0 and 1, the weather for bit 2 (asked
/// again every 30 min while it stays set; the client caches), sends limit and spend snapshots for bit 3 and then their changes, and
/// refreshes GitHub for bit 4 (at most once a minute) and sends BLOB GITHUB after each refresh that changed it. Sessions are sent on
/// every change whatever the mask. Approvals go out as urgent BLOB PERM_ASK, their end as TEXT PERM_GONE. Blob sends and network
/// calls never run on the caller's thread (the session's read thread calls <see cref="OnWatch"/>). A send that fails because the
/// link died is dropped; the session notices the dead link itself.
/// </summary>
internal sealed class IntegrationHost : IDisposable
{
    public const int WidgetsBit = StatsReporter.WidgetsBit;
    public const int ScreenBit = StatsReporter.ScreenBit;
    public const int WeatherBit = 4;
    public const int AiBit = CodexWatcher.AiBit;
    public const int GitHubBit = 16;
    public static readonly TimeSpan WeatherEvery = WeatherClient.ForecastFor;

    private readonly Action<Frame> send;
    private readonly BlobSender blobs;
    private readonly IntegrationSettings settings;
    private readonly DashboardParts parts;
    private readonly IStatsSource? stats;
    private readonly Func<DateTimeOffset> clock;
    private readonly Func<Action, TimeSpan, IDisposable> startTimer;
    private readonly Action<Action> run;
    private readonly Lock gate = new();
    private readonly CancellationTokenSource ended = new();

    private StatsReporter? reporter;
    private IDisposable? weatherTimer;
    private IDisposable? githubTimer;
    private DateTimeOffset? githubLast;
    private string? weatherSent;
    private int watch;
    private volatile bool disposed;

    /// <param name="send">Writes one frame to this phone. May throw once the link is dead.</param>
    /// <param name="blobs">This session's blob sender.</param>
    /// <param name="stats">The laptop's numbers, when this platform has a source.</param>
    /// <param name="startTimer">Runs the action now and then every interval on another thread until disposed. Tests pass their own.</param>
    /// <param name="run">Runs blocking work (a blob, a network call) off the caller's thread. Tests run it inline.</param>
    public IntegrationHost(
        Action<Frame> send,
        BlobSender blobs,
        IntegrationSettings settings,
        DashboardParts parts,
        IStatsSource? stats = null,
        Func<DateTimeOffset>? clock = null,
        Func<Action, TimeSpan, IDisposable>? startTimer = null,
        Action<Action>? run = null)
    {
        this.send = send;
        this.blobs = blobs;
        this.settings = settings;
        this.parts = parts;
        this.stats = stats;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.startTimer = startTimer ?? ((action, every) => new Timer(_ => action(), null, TimeSpan.Zero, every));
        this.run = run ?? (work => Task.Run(work));

        if (parts.Sessions is { } sessions)
        {
            sessions.Changed += SendSessions;
        }

        if (parts.Limits is { } limits)
        {
            limits.Changed += LimitsChanged;
        }

        if (parts.Spend is { } spend)
        {
            spend.Changed += SpendChanged;
        }

        if (parts.Broker is { } broker)
        {
            broker.Asked += Ask;
            broker.Gone += Gone;
        }
    }

    private bool AgentsOn => settings.Flag(IntegrationSettings.Claude) || settings.Flag(IntegrationSettings.Codex);

    /// <summary>Call once the session has sent FEATURES: sends the current sessions and lets the Codex watcher see the new phone.</summary>
    public void Start()
    {
        parts.Codex?.Evaluate();
        parts.Claude?.Evaluate();
        if (AgentsOn && parts.Sessions is not null)
        {
            SendSessions();
        }
    }

    /// <summary>The FEATURES tokens of the switches that are on and usable now.</summary>
    public IReadOnlyList<string> Features()
    {
        var features = new List<string>();
        if (settings.Flag(IntegrationSettings.Stats) && stats is not null)
        {
            features.Add(Feature.Stats);
        }

        if (settings.Flag(IntegrationSettings.Weather) && parts.Weather is not null && WeatherClient.CityFrom(settings.Text(IntegrationSettings.City)) is not null)
        {
            features.Add(Feature.Weather);
        }

        if (settings.Flag(IntegrationSettings.Claude) && parts.Sessions is not null)
        {
            features.Add(Feature.Claude);
        }

        if (settings.Flag(IntegrationSettings.Codex) && parts.Sessions is not null)
        {
            features.Add(Feature.Codex);
        }

        if (settings.Flag(IntegrationSettings.GitHub) && parts.GitHub is not null && parts.GitHubSignedIn?.Invoke() == true)
        {
            features.Add(Feature.GitHub);
        }

        return features;
    }

    /// <summary>The TEXT UNAVAILABLE entries (<c>feature:reason</c>) for switches that are on but cannot work here.</summary>
    public IReadOnlyList<string> Unavailable()
    {
        var reasons = new List<string>();
        if (settings.Flag(IntegrationSettings.Stats) && stats is null)
        {
            reasons.Add($"{Feature.Stats}:{UnavailableReason.Unsupported}");
        }

        if (settings.Flag(IntegrationSettings.GitHub) && parts.GitHubSignedIn?.Invoke() != true)
        {
            reasons.Add($"{Feature.GitHub}:{UnavailableReason.NoPermission}");
        }

        return reasons;
    }

    /// <summary>The phone's DASH_WATCH, 0..31 (the Dispatcher drops anything larger).</summary>
    public void OnWatch(int mask)
    {
        bool aiGained;
        bool githubGained;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            var before = watch;
            watch = mask;
            aiGained = (before & AiBit) == 0 && (mask & AiBit) != 0;
            githubGained = (before & GitHubBit) == 0 && (mask & GitHubBit) != 0;

            if ((mask & (WidgetsBit | ScreenBit)) != 0 && reporter is null && stats is not null && settings.Flag(IntegrationSettings.Stats))
            {
                reporter = new StatsReporter(stats, Push, action => startTimer(action, TimeSpan.FromSeconds(1)));
            }

            reporter?.SetWatch(mask);

            if ((mask & WeatherBit) != 0 && weatherTimer is null && WeatherOn)
            {
                weatherTimer = startTimer(() => run(RefreshWeather), WeatherEvery);
            }
            else if ((mask & WeatherBit) == 0)
            {
                Stop(ref weatherTimer);
                weatherSent = null;
            }

            if ((mask & GitHubBit) != 0 && githubTimer is null && GitHubOn)
            {
                githubTimer = startTimer(() => run(RefreshGitHub), GitHubRefresh.Every);
            }
            else if ((mask & GitHubBit) == 0)
            {
                Stop(ref githubTimer);
            }
        }

        parts.Codex?.SetWatch(mask);
        parts.Claude?.SetWatch(mask);
        if (aiGained && AgentsOn)
        {
            SendLimits();
            SendSpend();
        }

        if (githubGained && GitHubOn && parts.GitHub!.Lines.Count > 0)
        {
            run(SendGitHub);
        }
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
            reporter?.Dispose();
            reporter = null;
            Stop(ref weatherTimer);
            Stop(ref githubTimer);
        }

        ended.Cancel();
        if (parts.Sessions is { } sessions)
        {
            sessions.Changed -= SendSessions;
        }

        if (parts.Limits is { } limits)
        {
            limits.Changed -= LimitsChanged;
        }

        if (parts.Spend is { } spend)
        {
            spend.Changed -= SpendChanged;
        }

        if (parts.Broker is { } broker)
        {
            broker.Asked -= Ask;
            broker.Gone -= Gone;

            // The platform no longer counts this phone, so with no phone left every waiting ask goes back to the agent's own prompt.
            broker.PhonesChanged();
        }

        parts.Codex?.Evaluate();
        parts.Claude?.Evaluate();
        ended.Dispose();
    }

    private bool WeatherOn => settings.Flag(IntegrationSettings.Weather) && parts.Weather is not null;

    private bool GitHubOn => settings.Flag(IntegrationSettings.GitHub) && parts.GitHub is not null;

    private bool Watching(int bit)
    {
        lock (gate)
        {
            return !disposed && (watch & bit) != 0;
        }
    }

    private static void Stop(ref IDisposable? timer)
    {
        timer?.Dispose();
        timer = null;
    }

    private void SendSessions()
    {
        if (parts.Sessions is { } sessions && !disposed)
        {
            Push(new Text((byte)TextKind.Sessions, DashWire.EncodeSessions(sessions.Snapshot())));
        }
    }

    private void LimitsChanged()
    {
        if (Watching(AiBit))
        {
            SendLimits();
        }
    }

    private void SpendChanged()
    {
        if (Watching(AiBit))
        {
            SendSpend();
        }
    }

    private void SendLimits()
    {
        if (parts.Limits is { } limits)
        {
            Push(new Text((byte)TextKind.Limits, DashWire.EncodeLimits(limits.Snapshot())));
        }
    }

    private void SendSpend()
    {
        if (parts.Spend is { } spend)
        {
            Push(new Text((byte)TextKind.Spend, DashWire.EncodeSpend(spend.Snapshot())));
        }
    }

    private void Ask(PermPrompt prompt)
    {
        if (disposed)
        {
            return;
        }

        var body = Encoding.UTF8.GetBytes(prompt.Body);
        var label = DashWire.PermLabel(prompt.Id, prompt.Complete);
        run(() => Guard(() => blobs.Send(BlobKind.PermAsk, label, body, urgent: true)));
    }

    private void Gone(string id) => Push(new Text((byte)TextKind.PermGone, id));

    private void RefreshWeather()
    {
        if (parts.Weather is not { } weather || !Watching(WeatherBit))
        {
            return;
        }

        WeatherInfo? info;
        try
        {
            info = weather.GetAsync(settings.Text(IntegrationSettings.City), ended.Token).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        var text = DashWire.EncodeWeather(info);
        lock (gate)
        {
            if (text == weatherSent || disposed)
            {
                return;
            }

            weatherSent = text;
        }

        Push(new Text((byte)TextKind.Weather, text));
    }

    private void RefreshGitHub()
    {
        if (parts.GitHub is not { } github)
        {
            return;
        }

        lock (gate)
        {
            if (!GitHubRefresh.ShouldRun(githubLast, clock(), !disposed && (watch & GitHubBit) != 0))
            {
                return;
            }

            githubLast = clock();
        }

        GitHubRefreshResult result;
        try
        {
            result = github.RefreshAsync(ended.Token).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        if (result.Changed && Watching(GitHubBit))
        {
            SendGitHub();
        }
    }

    private void SendGitHub()
    {
        if (parts.GitHub is { } github)
        {
            var body = Encoding.UTF8.GetBytes(GitHubBlob.Encode(github.Lines));
            Guard(() => blobs.Send(BlobKind.Github, "gh", body));
        }
    }

    private void Push(Text text) => Guard(() => send(text));

    // The link died under a send: the session sees that on its own read and ends; nothing to do here.
    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Dropped with the link.
        }
    }
}
