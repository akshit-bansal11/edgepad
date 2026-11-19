using Edgepad.Dashboard;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class StatsTests
{
    [Theory]
    [InlineData(100, 1000, 150, 1100, 50)]
    [InlineData(100, 1000, 200, 1100, 0)]
    [InlineData(100, 1000, 100, 1100, 100)]
    [InlineData(100, 1000, 100, 1000, 0)]
    [InlineData(100, 1000, 50, 900, 0)]
    [InlineData(100, 1000, 120, 1100, 80)]
    [InlineData(100, 1000, 300, 1100, 0)]
    public void CpuPercentIsTheBusyShareAndNeverNegative(long prevIdle, long prevTotal, long idle, long total, int expected) =>
        Assert.Equal(expected, CpuMeter.Percent(prevIdle, prevTotal, idle, total));

    [Fact]
    public void RateIsKibPerSecondAndTheFirstReadingIsZero()
    {
        var meter = new RateMeter();

        Assert.Equal(0, meter.Update(1_000_000, 1000));
        Assert.Equal(100, meter.Update(1_000_000 + 102_400, 2000));
        Assert.Equal(50, meter.Update(1_000_000 + 102_400 + 51_200, 3000));
    }

    [Fact]
    public void RateIsZeroWhenTheCounterWrapsOrTheClockStands()
    {
        var meter = new RateMeter();
        meter.Update(5000, 1000);

        Assert.Equal(0, meter.Update(100, 2000));
        Assert.Equal(0, meter.Update(200_000, 2000));
        Assert.Equal(96, meter.Update(299_000, 3000));
    }

    [Fact]
    public void LossIsUnknownThenTheShareOfTheLastTwentyPings()
    {
        var meter = new LossMeter();
        Assert.Equal(-1, meter.Percent);

        meter.Record(false);
        Assert.Equal(100, meter.Percent);
        meter.Record(true);
        Assert.Equal(50, meter.Percent);

        for (var i = 0; i < LossMeter.Window; i++)
        {
            meter.Record(true);
        }

        Assert.Equal(0, meter.Percent);
        meter.Record(false);
        Assert.Equal(5, meter.Percent);
    }

    [Fact]
    public void NothingRunsBeforeTheFirstNonZeroMask()
    {
        using var rig = new Rig();

        rig.Reporter.SetWatch(0);
        rig.Reporter.SetWatch(4 | 8 | 16);
        rig.Reporter.Tick();

        Assert.Equal(0, rig.TimersStarted);
        Assert.False(rig.Reporter.Running);
        Assert.Empty(rig.Sent);
        Assert.Equal(0, rig.Source.FastReads);
    }

    [Fact]
    public void AMaskOfZeroStopsTheTicks()
    {
        using var rig = new Rig();
        rig.Reporter.SetWatch(1);
        rig.Reporter.Tick();
        var reads = rig.Source.FastReads;

        rig.Reporter.SetWatch(0);
        rig.Reporter.Tick();

        Assert.Equal(1, rig.TimersStopped);
        Assert.False(rig.Reporter.Running);
        Assert.Equal(reads, rig.Source.FastReads);
    }

    [Fact]
    public void WatchingOnlyWidgetsNeverReadsDisks()
    {
        using var rig = new Rig();
        rig.Reporter.SetWatch(StatsReporter.WidgetsBit);

        for (var i = 0; i < 12; i++)
        {
            rig.Reporter.Tick();
        }

        Assert.Equal(0, rig.Source.DiskReads);
        Assert.DoesNotContain(rig.Sent, t => t.Kind == (byte)TextKind.Disks);
        Assert.Contains(rig.Sent, t => t.Kind == (byte)TextKind.Stats);
        Assert.Contains(rig.Sent, t => t.Kind == (byte)TextKind.StatsInfo);
    }

    [Fact]
    public void TheScreenReadsDisksAtOnceThenEveryTenthTick()
    {
        using var rig = new Rig();
        rig.Reporter.SetWatch(StatsReporter.ScreenBit);

        for (var i = 0; i < 21; i++)
        {
            rig.Reporter.Tick();
        }

        Assert.Equal(3, rig.Source.DiskReads);
        Assert.Single(rig.Sent, t => t.Kind == (byte)TextKind.Disks);
    }

    [Fact]
    public void GainingTheScreenRereadsDisksAtOnce()
    {
        using var rig = new Rig();
        rig.Reporter.SetWatch(StatsReporter.WidgetsBit);
        rig.Reporter.Tick();

        rig.Reporter.SetWatch(StatsReporter.WidgetsBit | StatsReporter.ScreenBit);
        rig.Reporter.Tick();

        Assert.Equal(1, rig.Source.DiskReads);
    }

    [Fact]
    public void AnIdenticalReadingIsNotSentAgain()
    {
        using var rig = new Rig();
        rig.Reporter.SetWatch(StatsReporter.WidgetsBit);

        rig.Reporter.Tick();
        rig.Reporter.Tick();
        rig.Reporter.Tick();

        Assert.Single(rig.Sent, t => t.Kind == (byte)TextKind.Stats);
        Assert.Single(rig.Sent, t => t.Kind == (byte)TextKind.StatsInfo);

        rig.Source.Cpu = 77;
        rig.Reporter.Tick();

        Assert.Equal(2, rig.Sent.Count(t => t.Kind == (byte)TextKind.Stats));
    }

    [Fact]
    public void ARestartedWatchSendsEverythingAgain()
    {
        using var rig = new Rig();
        rig.Reporter.SetWatch(StatsReporter.WidgetsBit);
        rig.Reporter.Tick();
        rig.Reporter.SetWatch(0);
        rig.Reporter.SetWatch(StatsReporter.WidgetsBit);
        rig.Reporter.Tick();

        Assert.Equal(2, rig.Sent.Count(t => t.Kind == (byte)TextKind.Stats));
        Assert.Equal(2, rig.Sent.Count(t => t.Kind == (byte)TextKind.StatsInfo));
    }

    [Fact]
    public void AThrowingSourcePublishesUnknownsAndTicksOn()
    {
        using var rig = new Rig();
        rig.Source.Throws = true;
        rig.Reporter.SetWatch(StatsReporter.WidgetsBit | StatsReporter.ScreenBit);

        rig.Reporter.Tick();
        rig.Reporter.Tick();

        var stats = Assert.Single(rig.Sent, t => t.Kind == (byte)TextKind.Stats);
        Assert.Equal("-1/-1/-1/-1/-1/-1/-1/-1/-1/-1/-1", stats.Value);
        Assert.DoesNotContain(rig.Sent, t => t.Kind is (byte)TextKind.StatsInfo or (byte)TextKind.Disks);

        rig.Source.Throws = false;
        rig.Reporter.Tick();
        Assert.Equal(2, rig.Sent.Count(t => t.Kind == (byte)TextKind.Stats));
    }

    [Fact]
    public void AStuckPingNeverBlocksATickAndNeverOverlaps()
    {
        using var rig = new Rig();
        rig.Source.PingGate = new TaskCompletionSource<int>();
        rig.Reporter.SetWatch(StatsReporter.WidgetsBit);

        for (var i = 0; i < 10; i++)
        {
            rig.Reporter.Tick();
        }

        Assert.True(SpinWait.SpinUntil(() => rig.Source.PingCalls >= 1, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, rig.Source.PingCalls);
        Assert.Single(rig.Sent, t => t.Kind == (byte)TextKind.Stats);

        rig.Source.Cpu = 5;
        rig.Reporter.Tick();
        Assert.Equal(2, rig.Sent.Count(t => t.Kind == (byte)TextKind.Stats));

        rig.Source.PingGate.SetResult(12);
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                rig.Reporter.Tick();
                return rig.Sent.Any(t => t.Kind == (byte)TextKind.Stats && t.Value.Split('/')[6] == "12");
            },
            TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void AFailedPingCountsAsLoss()
    {
        using var rig = new Rig();
        rig.Source.PingGate = new TaskCompletionSource<int>();
        rig.Source.PingGate.SetResult(-1);
        rig.Reporter.SetWatch(StatsReporter.WidgetsBit);
        rig.Reporter.Tick();

        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                rig.Reporter.Tick();
                return rig.Sent.Any(t => t.Kind == (byte)TextKind.Stats && t.Value.Split('/')[7] == "100");
            },
            TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ASendThatThrowsDoesNotEscape()
    {
        using var rig = new Rig();
        rig.Fail = true;
        rig.Reporter.SetWatch(StatsReporter.WidgetsBit);

        rig.Reporter.Tick();

        Assert.Empty(rig.Sent);
    }

    [Fact]
    public void TheSourceIsIdledWhenTheWatchStopsAndOnlyThen()
    {
        using var rig = new Rig();
        rig.Reporter.SetWatch(0);
        Assert.Equal(0, rig.Source.Idles);

        rig.Reporter.SetWatch(StatsReporter.WidgetsBit);
        rig.Reporter.Tick();
        rig.Reporter.SetWatch(StatsReporter.WidgetsBit | StatsReporter.ScreenBit);
        Assert.Equal(0, rig.Source.Idles);

        rig.Reporter.SetWatch(0);
        rig.Reporter.SetWatch(0);
        Assert.Equal(1, rig.Source.Idles);

        rig.Reporter.SetWatch(StatsReporter.WidgetsBit);
        rig.Reporter.Tick();
        Assert.Equal(2, rig.Source.FastReads);
    }

    [Fact]
    public async Task ATickWhileTheLastIsStillReadingDoesNothing()
    {
        using var rig = new Rig();
        using var hold = new ManualResetEventSlim();
        rig.Source.FastGate = hold;
        rig.Reporter.SetWatch(StatsReporter.WidgetsBit);

        var slow = Task.Run(rig.Reporter.Tick, TestContext.Current.CancellationToken);
        Assert.True(SpinWait.SpinUntil(() => rig.Source.FastReads == 1, TimeSpan.FromSeconds(5)));
        rig.Reporter.Tick();
        Assert.Equal(1, rig.Source.FastReads);

        hold.Set();
        await slow;
        Assert.Single(rig.Sent, t => t.Kind == (byte)TextKind.Stats);
    }

    private sealed class Rig : IDisposable
    {
        public Rig()
        {
            Reporter = new StatsReporter(Source, Send, Start, work => work());
        }

        public FakeSource Source { get; } = new();

        public StatsReporter Reporter { get; }

        public List<Text> Sent { get; } = [];

        public int TimersStarted { get; private set; }

        public int TimersStopped { get; private set; }

        public bool Fail { get; set; }

        public void Dispose() => Reporter.Dispose();

        private void Send(Text text)
        {
            if (Fail)
            {
                throw new IOException("dead socket");
            }

            Sent.Add(text);
        }

        private IDisposable Start(Action action)
        {
            TimersStarted++;
            return new Stopper(() => TimersStopped++);
        }
    }

    private sealed class Stopper(Action stopped) : IDisposable
    {
        public void Dispose() => stopped();
    }

    private sealed class FakeSource : IStatsSource
    {
        public int Cpu { get; set; } = 10;

        public bool Throws { get; set; }

        public int FastReads { get; private set; }

        public int DiskReads { get; private set; }

        private int pingCalls;

        public int PingCalls => Volatile.Read(ref pingCalls);

        public TaskCompletionSource<int>? PingGate { get; set; }

        public int Idles { get; private set; }

        public ManualResetEventSlim? FastGate { get; set; }

        public void Idle() => Idles++;

        public StatsSample ReadFast()
        {
            FastReads++;
            FastGate?.Wait(TimeSpan.FromSeconds(10));
            return Throws ? throw new InvalidOperationException("counter missing") : new StatsSample(Cpu, 20, 30, 40, 50, 60, -1, -1, 70, 80, 90);
        }

        public StatsInfo ReadInfo() => Throws ? throw new InvalidOperationException("no wmi") : new StatsInfo("cpu", 16384, "gpu", 8192, "net");

        public IReadOnlyList<DiskEntry> ReadDisks()
        {
            DiskReads++;
            return Throws ? throw new InvalidOperationException("no disks") : [new DiskEntry("C", 50, 500, 250)];
        }

        public async ValueTask<int> PingAsync(CancellationToken cancel)
        {
            Interlocked.Increment(ref pingCalls);
            if (PingGate is { } gate)
            {
                return await gate.Task;
            }

            await Task.Delay(Timeout.Infinite, cancel);
            return -1;
        }
    }
}
