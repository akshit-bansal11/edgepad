using Edgepad.Protocol;

namespace Edgepad.Power;

/// <summary>
/// Tells the phone the laptop's battery list and audio output: both at <see cref="Start"/>, then whenever a poll
/// reads text that differs from what was last sent, and at once on <see cref="Poke"/>. Nothing a reader or the
/// socket throws escapes: a lost report costs nothing, and the read loop ends the session on a dead socket.
/// </summary>
internal sealed class PowerReporter : IDisposable
{
    private readonly Func<IReadOnlyList<BatteryEntry>> readBatteries;
    private readonly Func<(string Kind, string Name)?> readOutput;
    private readonly Action<Text> send;
    private readonly TimeSpan batteryEvery;
    private readonly TimeSpan outputEvery;
    private readonly object gate = new();
    private Timer? batteryTimer;
    private Timer? outputTimer;
    private string? lastBatteries;
    private string? lastOutput;
    private bool disposed;

    public PowerReporter(
        Func<IReadOnlyList<BatteryEntry>> readBatteries,
        Func<(string Kind, string Name)?> readOutput,
        Action<Text> send,
        TimeSpan batteryEvery,
        TimeSpan outputEvery)
    {
        this.readBatteries = readBatteries;
        this.readOutput = readOutput;
        this.send = send;
        this.batteryEvery = batteryEvery;
        this.outputEvery = outputEvery;
    }

    /// <summary>Sends both now, then polls on the two intervals.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (disposed || batteryTimer is not null)
            {
                return;
            }

            batteryTimer = new Timer(_ => PollBatteries(), null, TimeSpan.Zero, batteryEvery);
            outputTimer = new Timer(_ => PollOutput(), null, TimeSpan.Zero, outputEvery);
        }
    }

    /// <summary>A power or default-device event: reread both now.</summary>
    public void Poke()
    {
        PollBatteries();
        PollOutput();
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            batteryTimer?.Dispose();
            outputTimer?.Dispose();
        }
    }

    private void PollBatteries() =>
        Report(ref lastBatteries, () => BatteryText.Encode(readBatteries()), TextKind.Batteries);

    private void PollOutput() =>
        Report(ref lastOutput, () => readOutput() is { } o ? OutputText.Encode(o.Kind, o.Name) : string.Empty, TextKind.Output);

    private void Report(ref string? last, Func<string> read, TextKind kind)
    {
        try
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                var text = read();
                if (text == last)
                {
                    return;
                }

                send(new Text((byte)kind, text));
                last = text;
            }
        }
        catch (Exception)
        {
            // A reader or the socket failed; the next poll tries again.
        }
    }
}
