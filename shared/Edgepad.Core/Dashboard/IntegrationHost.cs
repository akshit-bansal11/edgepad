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

    /// <summary>Whether the agent's hook was heard since it was connected (<see cref="UsageStore.LastHeard"/> at least 0). Null counts every agent as heard.</summary>
    public Func<string, bool>? AgentHeard { get; init; }
}

/// <summary>
/// One phone link's side of the dashboard; a platform session makes one per link and disposes it when the link ends. Nothing runs
/// until the phone asks: <see cref="OnWatch"/> (DASH_WATCH) starts the stats reporter for bits 0 and 1, the weather for bit 2 (asked
/// again every 30 min while it stays set, and once more 2 min after a failed fetch; the client caches), sends limit and spend snapshots for bit 3 and then their changes, and
/// refreshes GitHub for bit 4 (at most once a minute) and sends BLOB GITHUB whenever it differs from what this phone was last sent,
/// and TEXT UNAVAILABLE again when GitHub refuses the token or first accepts a new one. Sessions are sent on
/// every change whatever the mask. Approvals go out as urgent BLOB PERM_ASK, their end as TEXT PERM_GONE. Blob sends and network
/// calls never run on the caller's thread (the session's read thread calls <see cref="OnWatch"/>). A send that fails because the
/// link died is dropped; the session notices the dead link itself.
/// </summary>
internal sealed class IntegrationHost : IDisposable
{
    public const int WidgetsBit = StatsReporter.WidgetsBit;
    public const int ScreenBit = StatsReporter.ScreenBit;
    public const int NetBit = StatsReporter.NetBit;
    public const int WeatherBit = 4;
    public const int AiBit = CodexWatcher.AiBit;
    public const int GitHubBit = 16;
    public static readonly TimeSpan WeatherEvery = WeatherClient.ForecastFor;

    // The last MaxGone ask ids that ended, so a PERM_ASK blob still queued for one is never sent.
    private const int MaxGone = 64;

    // A refresh this recent already answers a sign-in, so SignedInChanged does not ask GitHub twice.
    private static readonly TimeSpan JustRefreshed = TimeSpan.FromSeconds(5);

    // What a phone that has been sent no GitHub lines holds.
    private static readonly string NothingSent = GitHubBlob.Encode([]);

    private readonly Action<Frame> send;
    private readonly BlobSender blobs;
    private readonly IntegrationSettings settings;
    private readonly DashboardParts parts;
    private readonly IStatsSource? stats;
    private readonly INetInfoSource? net;
    private readonly SpeedTest speed;
    private readonly Func<DateTimeOffset> clock;
    private readonly Func<Action, TimeSpan, IDisposable> startTimer;
    private readonly Func<Action, TimeSpan, IDisposable> startOnce;
    private readonly Action<Action> run;
    private readonly Lock gate = new();
    private readonly CancellationTokenSource ended = new();

    // The last MaxGone ask ids that ended, so a PERM_ASK blob still queued for one is never sent.
    private readonly HashSet<string> gone = [];
    private readonly Queue<string> goneOrder = new();

    private StatsReporter? reporter;
    private IDisposable? weatherTimer;
    private IDisposable? weatherRetry;
    private IDisposable? githubTimer;
    private DateTimeOffset? githubLast;
    private string githubSent = NothingSent;
    private bool githubRefused;
    private string? weatherSent;
    private int watch;
    private volatile bool disposed;

    /// <param name="send">Writes one frame to this phone. May throw once the link is dead.</param>
    /// <param name="blobs">This session's blob sender.</param>
    /// <param name="stats">The laptop's numbers, when this platform has a source.</param>
    /// <param name="startTimer">Runs the action now and then every interval on another thread until disposed. Tests pass their own.</param>
    /// <param name="run">Runs blocking work (a blob, a network call) off the caller's thread. Tests run it inline.</param>
    /// <param name="startOnce">Runs the action once after the delay on another thread, unless disposed first. Tests pass their own.</param>
    /// <param name="net">The laptop's connection facts, when this platform has them; with it the phone gets NET_INFO and may run a speed test.</param>
    /// <param name="speedTest">The speed test runner; tests pass one with a fake transport.</param>
    public IntegrationHost(
        Action<Frame> send,
        BlobSender blobs,
        IntegrationSettings settings,
        DashboardParts parts,
        IStatsSource? stats = null,
        Func<DateTimeOffset>? clock = null,
        Func<Action, TimeSpan, IDisposable>? startTimer = null,
        Action<Action>? run = null,
        Func<Action, TimeSpan, IDisposable>? startOnce = null,
        INetInfoSource? net = null,
        SpeedTest? speedTest = null)
    {
        this.send = send;
        this.blobs = blobs;
        this.settings = settings;
        this.parts = parts;
        this.stats = stats;
        this.net = net;
        speed = speedTest ?? new SpeedTest();
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.startTimer = startTimer ?? ((action, every) => new Timer(_ => action(), null, TimeSpan.Zero, every));
        this.startOnce = startOnce ?? ((action, due) => new Timer(_ => action(), null, due, Timeout.InfiniteTimeSpan));
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

    private bool Heard(string agent) => parts.AgentHeard?.Invoke(agent) ?? true;

    private bool AgentsOn => settings.Active(IntegrationSettings.Claude) || settings.Active(IntegrationSettings.Codex);

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
        if (settings.Flag(IntegrationSettings.Developer))
        {
            features.Add(Feature.Developer);
        }

        if (settings.Flag(IntegrationSettings.Stats) && stats is not null)
        {
            features.Add(Feature.Stats);
            if (net is not null)
            {
                features.Add(Feature.NetInfo);
            }
        }

        if (settings.Flag(IntegrationSettings.Weather) && parts.Weather is not null && WeatherClient.CityFrom(settings.Text(IntegrationSettings.City)) is not null)
        {
            features.Add(Feature.Weather);
        }

        // An agent counts once it actually works with Edgepad (heard from, Codex trusted), not as soon as its switch is on.
        if (settings.Active(IntegrationSettings.Claude) && parts.Sessions is not null && Heard(AgentToken.Claude))
        {
            features.Add(Feature.Claude);
        }

        if (settings.Active(IntegrationSettings.Codex) && parts.Sessions is not null && Heard(AgentToken.Codex) && settings.Flag(IntegrationSettings.CodexTrusted))
        {
            features.Add(Feature.Codex);
        }

        if (settings.Active(IntegrationSettings.GitHub) && parts.GitHub is not null && parts.GitHubSignedIn?.Invoke() == true)
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

        if (settings.Active(IntegrationSettings.GitHub) && parts.GitHubSignedIn?.Invoke() != true)
        {
            reasons.Add($"{Feature.GitHub}:{UnavailableReason.NoPermission}");
        }

        return reasons;
    }

    /// <summary>The phone's DASH_WATCH, 0..63 (the Dispatcher drops anything larger).</summary>
    public void OnWatch(int mask)
    {
        bool aiGained;
        bool githubGained;
        bool netGained;
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
            netGained = (before & NetBit) == 0 && (mask & NetBit) != 0;

            if ((mask & (WidgetsBit | ScreenBit | NetBit)) != 0 && reporter is null && stats is not null && settings.Flag(IntegrationSettings.Stats))
            {
                reporter = new StatsReporter(stats, Push, action => startTimer(action, TimeSpan.FromSeconds(1)), net: net);
            }

            reporter?.SetWatch(mask);

            if ((mask & WeatherBit) != 0 && weatherTimer is null && WeatherOn)
            {
                weatherTimer = startTimer(() => run(RefreshWeather), WeatherEvery);
            }
            else if ((mask & WeatherBit) == 0)
            {
                Stop(ref weatherTimer);
                Stop(ref weatherRetry);
                weatherSent = null;
            }

            if ((mask & GitHubBit) != 0 && githubTimer is null && GitHubOn)
            {
                githubTimer = startTimer(() => run(RefreshGitHub), GitHubRefresh.Every);
            }
            else if ((mask & GitHubBit) == 0)
            {
                Stop(ref githubTimer);
                githubSent = NothingSent;
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

        if (netGained && speed.Last is { } last)
        {
            Push(new Text((byte)TextKind.SpeedResult, SpeedWire.Encode(last)));
        }
    }

    /// <summary>
    /// The phone's SPEED_TEST: true starts one speed test (ignored while one runs, and when this laptop does not name
    /// <see cref="Feature.NetInfo"/>), false cancels the running one. The result is pushed as TEXT SPEED_RESULT. Never blocks the caller.
    /// </summary>
    public void OnSpeedTest(bool start)
    {
        if (disposed)
        {
            return;
        }

        if (!start)
        {
            speed.Cancel();
        }
        else if (net is not null && stats is not null && settings.Flag(IntegrationSettings.Stats))
        {
            speed.Start(result => Push(new Text((byte)TextKind.SpeedResult, SpeedWire.Encode(result))));
        }
    }

    /// <summary>
    /// The app's GitHub sign-in finished or its token went (call it after FEATURES has been sent again). While the phone watches
    /// bit 4 and GitHub is on: signed in refreshes at once, bypassing the 60 s interval one time (the timer is started when
    /// the host has none), then sends the blob; signed out says UNAVAILABLE no-permission. Does nothing when the phone is not
    /// watching, and never starts a timer then.
    /// </summary>
    public void SignedInChanged()
    {
        var refresh = false;
        var refused = false;
        lock (gate)
        {
            if (disposed || (watch & GitHubBit) == 0 || !GitHubOn)
            {
                return;
            }

            if (parts.GitHubSignedIn?.Invoke() != true)
            {
                refused = !githubRefused;
                githubRefused = true;
            }
            else if (githubTimer is null)
            {
                // The new timer runs the first refresh now.
                githubTimer = startTimer(() => run(RefreshGitHub), GitHubRefresh.Every);
            }
            else if (githubLast is not { } last || clock() - last >= JustRefreshed)
            {
                githubLast = null;
                refresh = true;
            }
        }

        if (refused)
        {
            Push(new Text((byte)TextKind.Unavailable, string.Join('/', Unavailable())));
        }

        if (refresh)
        {
            run(RefreshGitHub);
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
            speed.Dispose();
            Stop(ref weatherTimer);
            Stop(ref weatherRetry);
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

    private bool GitHubOn => settings.Active(IntegrationSettings.GitHub) && parts.GitHub is not null;

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
        var id = prompt.Id;

        // An ask that ended before (or while) its blob went out is never completed: the phone would show a sheet nobody waits on.
        run(() => Guard(() =>
        {
            if (!Ended(id))
            {
                blobs.Send(BlobKind.PermAsk, label, body, abandoned: () => Ended(id), urgent: true);
            }
        }));
    }

    private void Gone(string id)
    {
        lock (gate)
        {
            if (gone.Add(id))
            {
                goneOrder.Enqueue(id);
                while (goneOrder.Count > MaxGone)
                {
                    gone.Remove(goneOrder.Dequeue());
                }
            }
        }

        Push(new Text((byte)TextKind.PermGone, id));
    }

    private bool Ended(string id)
    {
        lock (gate)
        {
            return gone.Contains(id);
        }
    }

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
            if (disposed)
            {
                return;
            }

            // A failed fetch is asked once more when the client allows it again, not only at the next 30 min tick.
            Stop(ref weatherRetry);
            if (info is null && (watch & WeatherBit) != 0 && WeatherClient.CityFrom(settings.Text(IntegrationSettings.City)) is not null)
            {
                weatherRetry = startOnce(() => run(RefreshWeather), WeatherClient.RetryAfter);
            }

            if (text == weatherSent)
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

        // A revoked token says so at once (the phone shows its sign-in note); the first good refresh after it takes the note away.
        bool resendUnavailable;
        lock (gate)
        {
            var refused = result.Result == GitHubResult.NoPermission;
            resendUnavailable = !disposed && (refused ? !githubRefused : githubRefused && result.Result == GitHubResult.Ok);
            githubRefused = refused || (githubRefused && result.Result != GitHubResult.Ok);
        }

        if (resendUnavailable)
        {
            Push(new Text((byte)TextKind.Unavailable, string.Join('/', Unavailable())));
        }

        // Compared with what this phone was sent, not with the shared client's last refresh: another phone's refresh may have
        // fetched the change, leaving Changed false here.
        if (Watching(GitHubBit))
        {
            SendGitHub();
        }
    }

    private void SendGitHub()
    {
        if (parts.GitHub is { } github)
        {
            var text = GitHubBlob.Encode(github.Lines);
            lock (gate)
            {
                if (text == githubSent || disposed)
                {
                    return;
                }

                githubSent = text;
            }

            var body = Encoding.UTF8.GetBytes(text);
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
