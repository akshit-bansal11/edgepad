namespace Edgepad.Net;

/// <summary>
/// Ends a session whose phone has sent nothing for <c>limit</c>. A vanished peer on RFCOMM or TCP can hold the blocked
/// read open far longer than that, while its held keys, pad and ink stay down. The read loop calls <see cref="Touch"/>
/// after every frame; a timer checks the age and calls <c>onSilent</c> once, on the timer's thread, so it must be safe
/// to call from there while the loop is blocked in a read.
/// </summary>
internal sealed class SilenceWatch : IDisposable
{
    public static readonly TimeSpan PhoneSilence = TimeSpan.FromSeconds(30);

    private readonly long limitMs;
    private readonly Action onSilent;
    private readonly Func<long> clock;
    private readonly Timer? timer;
    private long last;
    private int fired;

    public SilenceWatch(TimeSpan limit, Action onSilent, Func<long>? clock = null, TimeSpan? every = null)
    {
        limitMs = (long)limit.TotalMilliseconds;
        this.onSilent = onSilent;
        this.clock = clock ?? (() => Environment.TickCount64);
        last = this.clock();
        if (every is { } period)
        {
            // An exception on a timer thread ends the process; onSilent tears a session down, which can throw.
            timer = new Timer(
                _ =>
                {
                    try
                    {
                        Check();
                    }
                    catch (Exception e)
                    {
                        Log.Write($"Silence watch failed: {e}");
                    }
                },
                null,
                period,
                period);
        }
    }

    public void Touch() => Volatile.Write(ref last, clock());

    internal bool Check()
    {
        if (clock() - Volatile.Read(ref last) <= limitMs || Interlocked.Exchange(ref fired, 1) != 0)
        {
            return false;
        }

        onSilent();
        return true;
    }

    public void Dispose() => timer?.Dispose();
}
