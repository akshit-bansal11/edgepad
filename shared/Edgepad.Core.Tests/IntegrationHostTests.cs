using System.Net;
using System.Text;
using Edgepad.Blob;
using Edgepad.Dashboard;
using Edgepad.Hooks;
using Edgepad.Net;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class IntegrationHostTests : IDisposable
{
    private const string GitHubToken = "gho_SecretTokenValue1234567890abcdef";

    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-host");
    private readonly FakeClock clock = new();
    private readonly List<Frame> frames = [];
    private readonly List<FakeTimer> timers = [];
    private readonly IntegrationSettings settings;
    private readonly SessionBoard sessions;
    private readonly UsageStore usage;
    private readonly PermissionBroker broker;
    private readonly FakeHttp github = new(_ => FakeHttp.Status(HttpStatusCode.InternalServerError));
    private readonly FakeHttp weather = new(_ => FakeHttp.Status(HttpStatusCode.InternalServerError));
    private readonly Secrets secrets = new();

    public IntegrationHostTests()
    {
        settings = new IntegrationSettings(Path.Combine(dir.FullName, IntegrationSettings.FileName));
        sessions = new SessionBoard(() => clock.Now);
        usage = new UsageStore(Path.Combine(dir.FullName, UsageStore.FileName), clock.Now);
        broker = new PermissionBroker(sessions, () => true, () => clock.Now, TimeSpan.FromSeconds(10));
        secrets.Set(GitHubApp.TokenName, GitHubToken);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => dir.Delete(recursive: true);

    private void AllOn()
    {
        foreach (var key in new[] { IntegrationSettings.Stats, IntegrationSettings.Weather, IntegrationSettings.Claude, IntegrationSettings.Codex, IntegrationSettings.GitHub })
        {
            settings.SetFlag(key, true);
        }

        settings.Set(IntegrationSettings.City, "Delhi");
    }

    private IntegrationHost New(bool withParts = true, bool withStats = true)
    {
        var parts = withParts
            ? new DashboardParts
            {
                Sessions = sessions,
                Limits = new LimitsBoard(usage),
                Spend = new SpendBoard(usage),
                Broker = broker,
                Weather = new WeatherClient(weather, () => clock.Now),
                GitHub = new GitHubApi(secrets, github, () => clock.Now),
                GitHubSignedIn = () => GitHubApp.IsToken(secrets.Get(GitHubApp.TokenName)),
            }
            : new DashboardParts();
        var blobs = new BlobSender(Record, LinkKind.Bluetooth, () => 0, Ct, () => long.MaxValue, _ => true);
        return new IntegrationHost(Record, blobs, settings, parts, withStats ? new Source() : null, () => clock.Now, StartTimer, work => work());
    }

    private void Record(Frame frame)
    {
        lock (frames)
        {
            frames.Add(frame);
        }
    }

    private IDisposable StartTimer(Action action, TimeSpan every)
    {
        var timer = new FakeTimer(action, every);
        timers.Add(timer);
        return timer;
    }

    private List<Text> Texts(TextKind kind) => [.. frames.OfType<Text>().Where(t => t.Kind == (byte)kind)];

    private List<BlobBegin> Blobs(BlobKind kind) => [.. frames.OfType<BlobBegin>().Where(b => b.Kind == (byte)kind)];

    [Fact]
    public void AllSwitchesOffNamesNothingAndStartsNothing()
    {
        using var host = New(withParts: false);
        Assert.Empty(host.Features());
        Assert.Empty(host.Unavailable());

        host.OnWatch(31);
        host.Start();

        Assert.Empty(timers);
        Assert.Empty(frames);
        Assert.Empty(github.Seen);
        Assert.Empty(weather.Seen);
    }

    [Fact]
    public void FeaturesNameOnlyUsableSwitches()
    {
        AllOn();
        using var host = New();
        Assert.Equal([Feature.Stats, Feature.Weather, Feature.Claude, Feature.Codex, Feature.GitHub], host.Features());

        secrets.Delete(GitHubApp.TokenName);
        using var noStats = New(withStats: false);
        Assert.DoesNotContain(Feature.GitHub, noStats.Features());
        Assert.DoesNotContain(Feature.Stats, noStats.Features());
        Assert.Equal([$"{Feature.Stats}:{UnavailableReason.Unsupported}", $"{Feature.GitHub}:{UnavailableReason.NoPermission}"], noStats.Unavailable());
    }

    [Fact]
    public void MaskOneStartsTheReporterOnceAndMaskZeroStopsEverything()
    {
        AllOn();
        using var host = New();

        host.OnWatch(1);
        host.OnWatch(1);
        Assert.Single(timers, t => t.Every == TimeSpan.FromSeconds(1));

        host.OnWatch(31);
        Assert.Contains(timers, t => t.Every == IntegrationHost.WeatherEvery);
        Assert.Contains(timers, t => t.Every == GitHubRefresh.Every);

        host.OnWatch(0);
        Assert.All(timers, t => Assert.True(t.Disposed));
    }

    [Fact]
    public async Task GitHubRefreshesOnBit4AndNotAgainWithinAMinute()
    {
        AllOn();
        using var host = New();

        host.OnWatch(IntegrationHost.GitHubBit);
        var tick = Assert.Single(timers, t => t.Every == GitHubRefresh.Every);
        await Task.Run(tick.Fire, Ct);
        var asked = github.Seen.Count;
        Assert.True(asked > 0);

        await Task.Run(tick.Fire, Ct);
        host.OnWatch(0);
        host.OnWatch(IntegrationHost.GitHubBit);
        await Task.Run(timers[^1].Fire, Ct);
        Assert.Equal(asked, github.Seen.Count);
    }

    [Fact]
    public void WeatherIsAskedOnlyForBit2()
    {
        AllOn();
        using var host = New();
        host.OnWatch(IntegrationHost.WidgetsBit | IntegrationHost.AiBit | IntegrationHost.GitHubBit);
        Assert.DoesNotContain(timers, t => t.Every == IntegrationHost.WeatherEvery);

        host.OnWatch(IntegrationHost.WeatherBit);
        Assert.Single(timers, t => t.Every == IntegrationHost.WeatherEvery && !t.Disposed);
    }

    [Fact]
    public async Task AnAskGoesOutUrgentlyAndItsAnswerEndsIt()
    {
        AllOn();
        using var host = New();

        var request = broker.RequestAsync(new PermRequest("T", AgentToken.Claude, "s1", "/w", "Bash", new ToolInput("ls -la", null, null)), Ct);
        var begin = Assert.Single(Blobs(BlobKind.PermAsk));
        Assert.True(DashWire.TryParsePermLabel(begin.Label, out var id, out var complete));
        Assert.True(complete);
        Assert.Equal((uint)Encoding.UTF8.GetByteCount("claude\nBash\nls -la"), begin.Size);

        Assert.True(broker.Answer(id + "/a"));
        Assert.Equal(PermDecision.Allow, await request);
        Assert.Equal(id, Assert.Single(Texts(TextKind.PermGone)).Value);
        Assert.False(broker.Answer(id + "/a"));
        Assert.False(broker.Answer("garbage"));
    }

    [Fact]
    public void SessionsAreSentOnEveryChangeWhateverTheMask()
    {
        AllOn();
        using var host = New();
        host.Start();
        Assert.Equal(string.Empty, Assert.Single(Texts(TextKind.Sessions)).Value);

        sessions.Apply(new HookEvent(AgentToken.Claude, "s1", SessionEvent.Prompt, "/work/app"));
        Assert.Equal("claude:busy:app", Texts(TextKind.Sessions)[^1].Value);
    }

    [Fact]
    public void Bit3SendsLimitAndSpendSnapshotsThenTheirChanges()
    {
        AllOn();
        using var host = New();
        new LimitsBoard(usage).Update(AgentToken.Claude, [new RawLimit("five_hour", 12, null)], null, clock.Now);
        Assert.Empty(Texts(TextKind.Limits));

        host.OnWatch(IntegrationHost.AiBit);
        Assert.Single(Texts(TextKind.Limits));
        Assert.Single(Texts(TextKind.Spend));
        Assert.StartsWith("claude:300:12:", Texts(TextKind.Limits)[0].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void DisposeStopsTimersAndListening()
    {
        AllOn();
        var host = New();
        host.OnWatch(31);
        host.Dispose();

        Assert.NotEmpty(timers);
        Assert.All(timers, t => Assert.True(t.Disposed));
        var before = frames.Count;
        sessions.Apply(new HookEvent(AgentToken.Claude, "s1", SessionEvent.Prompt, "/work/app"));
        host.OnWatch(31);
        Assert.Equal(before, frames.Count);
    }

    [Fact]
    public async Task DisposingTheLastPhoneDefersItsWaitingAsks()
    {
        AllOn();
        var linked = true;
        var lone = new PermissionBroker(sessions, () => linked, () => clock.Now, TimeSpan.FromSeconds(10));
        var blobs = new BlobSender(Record, LinkKind.Bluetooth, () => 0, Ct, () => long.MaxValue, _ => true);
        var host = new IntegrationHost(Record, blobs, settings, new DashboardParts { Sessions = sessions, Broker = lone }, null, () => clock.Now, StartTimer, work => work());

        var request = lone.RequestAsync(new PermRequest("T", AgentToken.Claude, "s1", "/w", "Bash", new ToolInput("ls", null, null)), Ct);
        linked = false;
        host.Dispose();

        Assert.Equal(PermDecision.Defer, await request);
    }

    private sealed class FakeTimer(Action action, TimeSpan every) : IDisposable
    {
        public TimeSpan Every { get; } = every;

        public bool Disposed { get; private set; }

        public void Fire() => action();

        public void Dispose() => Disposed = true;
    }

    private sealed class Source : IStatsSource
    {
        public StatsSample ReadFast() => new(1, 2, 3, 4, 5, 6, -1, -1, 7, 8, 9);

        public StatsInfo ReadInfo() => new("cpu", 1024, "gpu", 512, "net");

        public IReadOnlyList<DiskEntry> ReadDisks() => [];

        public ValueTask<int> PingAsync(CancellationToken cancel) => ValueTask.FromResult(1);
    }

    private sealed class Secrets : ISecretStore
    {
        private readonly Dictionary<string, string> values = [];

        public string? Get(string name) => values.GetValueOrDefault(name);

        public bool Set(string name, string value)
        {
            values[name] = value;
            return true;
        }

        public void Delete(string name) => values.Remove(name);
    }
}
