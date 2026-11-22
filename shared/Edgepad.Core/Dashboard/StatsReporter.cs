using System.Diagnostics;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// Sends the phone the laptop's numbers, and only while the phone draws them. <see cref="SetWatch"/> takes DASH_WATCH: bit 5 (the Network page) also
/// sends NET_INFO, read each tick and sent only when it changed, when the platform has an <see cref="INetInfoSource"/>. Bit 0
/// (widgets) or bit 1 (System screen) starts a one-second timer, a mask with none of bits 0, 1 and 5 stops it (and lets the source release what it
/// holds, <see cref="IStatsSource.Idle"/>), and with the timer stopped nothing is read, pinged or sent. Ping runs every third tick
/// (never two at once, a stuck one is cancelled after 2 s), disks every tenth and only for bit 1, on a worker (never two at once,
/// a reading that took over 2 s is dropped), names every thirtieth. Readings are taken outside the lock, and a tick that finds the
/// previous one still running does nothing. A text identical to the last one sent is not sent again. Nothing a source or the
/// socket throws escapes: an unreadable source publishes unknowns and is logged once.
/// </summary>
internal sealed class StatsReporter : IDisposable
{
    public const int WidgetsBit = 1;
    public const int ScreenBit = 2;
    public const int NetBit = 32;

    private const int PingEvery = 3;
    private const int DisksEvery = 10;
    private const int InfoEvery = 30;
    private static readonly TimeSpan PingLimit = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DisksLimit = TimeSpan.FromSeconds(2);

    private readonly IStatsSource source;
    private readonly INetInfoSource? net;
    private readonly Action<Text> send;
    private readonly Func<Action, IDisposable> startTimer;
    private readonly Action<Action> worker;
    private readonly Lock gate = new();

    // Held for a whole tick; Idle takes it too, so the source is never released under a reading. Taken before gate, never after.
    private readonly Lock tickGate = new();
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
    private bool disksRunning;
    private bool idlePending;
    private bool logged;
    private bool disposed;
    private string? lastStats;
    private string? lastInfo;
    private string? lastDisks;
    private string? lastNet;

    /// <param name="startTimer">Runs the action every second until disposed, on another thread, never inline. Tests pass their own.</param>
    /// <param name="worker">Runs the disk reading off the tick. Tests run it inline.</param>
    public StatsReporter(IStatsSource source, Action<Text> send, Func<Action, IDisposable>? startTimer = null, Action<Action>? worker = null, INetInfoSource? net = null)
    {
        this.source = source;
        this.net = net;
        this.send = send;
        this.startTimer = startTimer ?? (action => new Timer(_ => action(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        this.worker = worker ?? (work => Task.Run(work));
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

    /// <summary>Applies the phone's DASH_WATCH. Anything outside 0..63 is the caller's to drop; only bits 0, 1 and 5 matter here.</summary>
    public void SetWatch(int watch)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            var wanted = watch & (WidgetsBit | ScreenBit | NetBit);
            var before = mask;
            mask = wanted;
            if (wanted == 0)
            {
                Stop();
            }
            else if (timer is null)
            {
                generation++;
                idlePending = false;
                (pingIn, disksIn, infoIn) = (0, 0, 0);
                (lastStats, lastInfo, lastDisks, lastNet) = (null, null, null, null);
                timer = startTimer(Tick);
            }
            else
            {
                if ((before & ScreenBit) == 0 && (wanted & ScreenBit) != 0)
                {
                    disksIn = 0;
                    lastDisks = null;
                }

                if ((before & NetBit) == 0 && (wanted & NetBit) != 0)
                {
                    lastNet = null; // a phone that just opened the page has none yet
                }
            }
        }

        IdleIfStopped();
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            Stop();
        }

        IdleIfStopped();
    }

    /// <summary>One second's work. Public to the timer and to tests.</summary>
    internal void Tick()
    {
        if (!tickGate.TryEnter())
        {
            return; // the previous tick is still reading
        }

        try
        {
            int mine;
            int ping;
            int lossPct;
            bool info;
            bool disks;
            bool netNow;
            lock (gate)
            {
                if (disposed || mask == 0)
                {
                    return;
                }

                StartPingIfDue();
                mine = generation;
                netNow = net is not null && (mask & NetBit) != 0;
                ping = lastPing;
                lossPct = loss.Percent;
                info = --infoIn <= 0;
                if (info)
                {
                    infoIn = InfoEvery;
                }

                // A reading still running (a slow network mount) is waited for: tried again on the next tick.
                disks = (mask & ScreenBit) != 0 && --disksIn <= 0 && !disksRunning;
                if (disks)
                {
                    disksIn = DisksEvery;
                    disksRunning = true;
                }
            }

            var stats = ReadStats(ping, lossPct);
            var infoText = info ? Read(() => DashWire.EncodeStatsInfo(source.ReadInfo())) : null;
            var netText = netNow ? Read(() => NetInfoWire.Encode(net!.Read())) ?? NetInfoWire.Encode(NetInfo.Unknown) : null;
            lock (gate)
            {
                if (!disposed && mine == generation)
                {
                    SendIfChanged(TextKind.Stats, stats, ref lastStats);
                    if (infoText is not null)
                    {
                        SendIfChanged(TextKind.StatsInfo, infoText, ref lastInfo);
                    }

                    if (netText is not null)
                    {
                        SendIfChanged(TextKind.NetInfo, netText, ref lastNet);
                    }
                }
            }

            if (disks)
            {
                StartDisks(mine);
            }
        }
        finally
        {
            tickGate.Exit();
        }
    }

    private void Stop()
    {
        generation++;
        idlePending |= timer is not null;
        timer?.Dispose();
        timer = null;
        pingCancel?.Cancel();
        pingCancel = null;
    }

    // Outside gate: waits for a running tick, then lets the source let go while the timer is still stopped.
    private void IdleIfStopped()
    {
        lock (tickGate)
        {
            lock (gate)
            {
                if (!idlePending || timer is not null)
                {
                    return;
                }

                idlePending = false;
            }

            Read(() =>
            {
                source.Idle();
                return string.Empty;
            });
        }
    }

    private string ReadStats(int ping, int lossPct)
    {
        StatsSample sample;
        try
        {
            sample = source.ReadFast() with { Ping = ping, Loss = lossPct };
        }
        catch (Exception)
        {
            LogOnce();
            sample = new StatsSample(-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1);
        }

        return DashWire.EncodeStats(sample);
    }

    private void StartDisks(int mine)
    {
        var started = Stopwatch.GetTimestamp();
        worker(() =>
        {
            var text = Read(() => DashWire.EncodeDisks(source.ReadDisks()));
            lock (gate)
            {
                disksRunning = false;
                if (text is not null && !disposed && mine == generation && (mask & ScreenBit) != 0
                    && Stopwatch.GetElapsedTime(started) <= DisksLimit)
                {
                    SendIfChanged(TextKind.Disks, text, ref lastDisks);
                }
            }
        });
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

    // The reading's text, or null (logged once) when the source threw.
    private string? Read(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            LogOnce();
            return null;
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
