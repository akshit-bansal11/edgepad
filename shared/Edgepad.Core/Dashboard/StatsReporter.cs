using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// Sends the phone the laptop's numbers, and only while the phone draws them. <see cref="SetWatch"/> takes DASH_WATCH: bit 0
/// (widgets) or bit 1 (System screen) starts a one-second timer, a mask with neither stops it, and with the timer stopped nothing
/// is read, pinged or sent. Ping runs every third tick (never two at once, a stuck one is cancelled after 2 s), disks every tenth
/// and only for bit 1, names every thirtieth. A text identical to the last one sent is not sent again. Nothing a source or the
/// socket throws escapes: an unreadable source publishes unknowns and is logged once.
/// </summary>
internal sealed class StatsReporter : IDisposable
{
    public const int WidgetsBit = 1;
    public const int ScreenBit = 2;

    private const int PingEvery = 3;
    private const int DisksEvery = 10;
    private const int InfoEvery = 30;
    private static readonly TimeSpan PingLimit = TimeSpan.FromSeconds(2);

    private readonly IStatsSource source;
    private readonly Action<Text> send;
    private readonly Func<Action, IDisposable> startTimer;
    private readonly Lock gate = new();
    private readonly LossMeter loss = new();
    private IDisposable? timer;
    private CancellationTokenSource? pingCancel;
    private int mask;
    private int generation;
    private int pingIn;
    private int disksIn;
    private int infoIn;
    private int lastPing = -1;
    private bool pingRunning;
    private bool logged;
    private bool disposed;
    private string? lastStats;
    private string? lastInfo;
    private string? lastDisks;

    /// <param name="startTimer">Runs the action every second until disposed, on another thread, never inline. Tests pass their own.</param>
    public StatsReporter(IStatsSource source, Action<Text> send, Func<Action, IDisposable>? startTimer = null)
    {
        this.source = source;
        this.send = send;
        this.startTimer = startTimer ?? (action => new Timer(_ => action(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
    }

    /// <summary>True while the one-second timer exists.</summary>
    internal bool Running
    {
        get
        {
            lock (gate)
            {
                return timer is not null;
            }
        }
    }

    /// <summary>Applies the phone's DASH_WATCH. Anything outside 0..31 is the caller's to drop; only bits 0 and 1 matter here.</summary>
    public void SetWatch(int watch)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            var wanted = watch & (WidgetsBit | ScreenBit);
            var before = mask;
            mask = wanted;
            if (wanted == 0)
            {
                Stop();
                return;
            }

            if (timer is null)
            {
                generation++;
                (pingIn, disksIn, infoIn) = (0, 0, 0);
                (lastStats, lastInfo, lastDisks) = (null, null, null);
                timer = startTimer(Tick);
            }
            else if ((before & ScreenBit) == 0 && (wanted & ScreenBit) != 0)
            {
                disksIn = 0;
                lastDisks = null;
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            Stop();
        }
    }

    /// <summary>One second's work. Public to the timer and to tests.</summary>
    internal void Tick()
    {
        lock (gate)
        {
            if (disposed || mask == 0)
            {
                return;
            }

            StartPingIfDue();
            SendIfChanged(TextKind.Stats, ReadStats(), ref lastStats);
            if (--infoIn <= 0)
            {
                infoIn = InfoEvery;
                Guard(() => SendIfChanged(TextKind.StatsInfo, DashWire.EncodeStatsInfo(source.ReadInfo()), ref lastInfo));
            }

            if ((mask & ScreenBit) != 0 && --disksIn <= 0)
            {
                disksIn = DisksEvery;
                Guard(() => SendIfChanged(TextKind.Disks, DashWire.EncodeDisks(source.ReadDisks()), ref lastDisks));
            }
        }
    }

    private void Stop()
    {
        generation++;
        timer?.Dispose();
        timer = null;
        pingCancel?.Cancel();
        pingCancel = null;
    }

    private string ReadStats()
    {
        StatsSample sample;
        try
        {
            sample = source.ReadFast() with { Ping = lastPing, Loss = loss.Percent };
        }
        catch (Exception)
        {
            LogOnce();
            sample = new StatsSample(-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1);
        }

        return DashWire.EncodeStats(sample);
    }

    private void StartPingIfDue()
    {
        if (--pingIn > 0)
        {
            return;
        }

        pingIn = PingEvery;
        if (pingRunning)
        {
            return;
        }

        pingRunning = true;
        var mine = generation;
        var cancel = new CancellationTokenSource(PingLimit);
        pingCancel = cancel;
        _ = Task.Run(async () =>
        {
            var ms = -1;
            try
            {
                ms = await source.PingAsync(cancel.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A failed or cancelled ping is a lost packet, nothing more.
            }

            lock (gate)
            {
                pingRunning = false;
                if (pingCancel == cancel)
                {
                    pingCancel = null;
                }

                if (mine == generation)
                {
                    loss.Record(ms >= 0);
                    lastPing = ms >= 0 ? ms : -1;
                }
            }

            cancel.Dispose();
        });
    }

    private void SendIfChanged(TextKind kind, string text, ref string? last)
    {
        if (text == last)
        {
            return;
        }

        last = text;
        try
        {
            send(new Text((byte)kind, text));
        }
        catch (Exception)
        {
            // A lost report costs nothing: the read loop ends the session on a dead socket, and the next change is sent afresh.
        }
    }

    private void Guard(Action read)
    {
        try
        {
            read();
        }
        catch (Exception)
        {
            LogOnce();
        }
    }

    private void LogOnce()
    {
        if (!logged)
        {
            logged = true;
            Log.Write("stats source failed (no data)");
        }
    }
}
