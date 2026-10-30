using System.Diagnostics;
using System.Globalization;

namespace Edgepad.Dispatch;

/// <summary>
/// How evenly one session's Move frames arrived, as a count of the gaps between consecutive frames. It sees
/// spacing on this laptop only, never delay: a link that delivers late but evenly looks the same as one
/// that delivers on time. Read once when the session ends, so the owner can compare Bluetooth with Wi-Fi.
/// Single-threaded, like the dispatcher, and it allocates nothing per frame.
/// </summary>
internal sealed class PointerCadence
{
    private const int MinFrames = 50;

    private static readonly long FiveMs = Ticks(5);
    private static readonly long TwentyMs = Ticks(20);
    private static readonly long FiftyMs = Ticks(50);
    private static readonly long HundredMs = Ticks(100);

    // A longer pause than this is the finger lifting, not a gap in the stream, so it ends a run of motion.
    private static readonly long RunBreak = Ticks(250);

    private long last;
    private long frames;
    private long motionTicks;
    private long longest;
    private long under5;
    private long under20;
    private long under50;
    private long under100;
    private long over100;

    /// <summary>Notes one Move frame; <paramref name="timestamp"/> is <see cref="Stopwatch.GetTimestamp"/>.</summary>
    public void Record(long timestamp)
    {
        frames++;
        var gap = timestamp - last;
        last = timestamp;
        if (frames == 1 || gap > RunBreak)
        {
            return;
        }

        motionTicks += gap;
        longest = Math.Max(longest, gap);
        if (gap < FiveMs)
        {
            under5++;
        }
        else if (gap < TwentyMs)
        {
            under20++;
        }
        else if (gap < FiftyMs)
        {
            under50++;
        }
        else if (gap < HundredMs)
        {
            under100++;
        }
        else
        {
            over100++;
        }
    }

    /// <summary>
    /// The closing figures, or null when there were too few Move frames to say anything. The longest gap is
    /// the longest inside a run of motion, so a pause between strokes is not in it.
    /// </summary>
    public string? Summary() => frames < MinFrames
        ? null
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{frames} in {motionTicks / (double)Stopwatch.Frequency:0.0} s of motion; gaps under 5 ms: {under5}, 5-20: {under20}, 20-50: {under50}, 50-100: {under100}, over 100: {over100}; longest {longest * 1000.0 / Stopwatch.Frequency:0} ms");

    private static long Ticks(long ms) => Stopwatch.Frequency * ms / 1000;
}
