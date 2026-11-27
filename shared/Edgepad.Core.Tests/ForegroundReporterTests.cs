using Edgepad.Foreground;
using Xunit;

namespace Edgepad.Tests;

public sealed class ForegroundReporterTests
{
    private readonly FakeSource source = new();
    private readonly List<string> sent = [];
    private readonly List<Action> timers = [];

    private ForegroundReporter NewReporter(Action<string>? send = null) =>
        new(source, send ?? sent.Add, (_, action) => timers.Add(action));

    private static ForegroundApp App(string name) => ForegroundApp.Clean(name.ToLowerInvariant(), name);

    private void Cool()
    {
        var due = timers.ToArray();
        timers.Clear();
        foreach (var timer in due)
        {
            timer();
        }
    }

    [Fact]
    public void NothingIsWatchedUntilAskedTo()
    {
        using var reporter = NewReporter();

        Assert.Equal(0, source.Watching);
        Assert.Empty(sent);
    }

    [Fact]
    public void WatchingSendsTheCurrentAppAtOnce()
    {
        source.Current = App("Chrome");
        using var reporter = NewReporter();

        reporter.Watch(true);

        Assert.Equal(["chrome/Chrome"], sent);
    }

    [Fact]
    public void ChangesInsideTheWindowCoalesceAndTheNewestWins()
    {
        using var reporter = NewReporter();
        reporter.Watch(true);
        source.Raise(App("One"));
        source.Raise(App("Two"));
        source.Raise(App("Three"));

        Assert.Equal(["one/One"], sent);

        Cool();

        Assert.Equal(["one/One", "three/Three"], sent);
    }

    [Fact]
    public void ARepeatOfWhatWasSentIsNotSentAgain()
    {
        using var reporter = NewReporter();
        reporter.Watch(true);
        source.Raise(App("One"));
        source.Raise(App("One"));
        Cool();

        Assert.Equal(["one/One"], sent);
    }

    [Fact]
    public void NothingIsSentAfterWatchFalse()
    {
        using var reporter = NewReporter();
        reporter.Watch(true);
        source.Raise(App("One"));
        source.Raise(App("Two"));
        reporter.Watch(false);
        Cool();
        source.Raise(App("Three"));

        Assert.Equal(["one/One"], sent);
        Assert.Equal(0, source.Watching);
    }

    [Fact]
    public void AThrowingSendIsSwallowed()
    {
        using var reporter = NewReporter(_ => throw new InvalidOperationException("dead link"));

        reporter.Watch(true);
        source.Raise(App("One"));
        Cool();
    }

    [Fact]
    public void AWatchStartedAgainSendsAfresh()
    {
        source.Current = App("One");
        using var reporter = NewReporter();
        reporter.Watch(true);
        reporter.Watch(false);
        Cool();
        reporter.Watch(true);

        Assert.Equal(["one/One", "one/One"], sent);
    }

    [Fact]
    public void AKnownTokenReachesTheKindSender()
    {
        List<string> kinds = [];
        source.Current = App("spotify.exe");
        using var reporter = new ForegroundReporter(source, sent.Add, (_, action) => timers.Add(action), new ForegroundKind("windows", (a, b) => a == b, kinds.Add));

        reporter.Watch(true);

        Assert.Equal(["media"], kinds);
    }

    private sealed class FakeSource : IForegroundApp
    {
        private Action<ForegroundApp>? onChange;

        public ForegroundApp Current { get; set; } = ForegroundApp.None;

        public int Watching { get; private set; }

        public bool Available => true;

        public void Raise(ForegroundApp app) => onChange?.Invoke(app);

        public IDisposable Watch(Action<ForegroundApp> change)
        {
            onChange = change;
            Watching++;
            change(Current);
            return new Stop(this);
        }

        private sealed class Stop(FakeSource owner) : IDisposable
        {
            public void Dispose()
            {
                owner.onChange = null;
                owner.Watching--;
            }
        }
    }
}
