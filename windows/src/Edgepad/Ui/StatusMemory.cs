using Edgepad.Net;

namespace Edgepad.Ui;

/// <summary>
/// What the header should say, given the statuses that arrive. A refusal is posted and then overwritten with
/// "Waiting for your phone" the moment the refused session ends, so on its own it would be on screen for an
/// instant; this keeps it until a phone connects or ten minutes pass. It also decides when a refusal earns a balloon:
/// once per distinct cause per hour.
/// </summary>
internal sealed class StatusMemory
{
    public static readonly TimeSpan Sticky = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan AnnounceEvery = TimeSpan.FromHours(1);

    private readonly Dictionary<string, long> announced = [];
    private string? problem;
    private long problemUntil;
    private string? quiet;
    private long quietUntil;

    /// <summary>The text to show for <paramref name="status"/> at <paramref name="now"/> (milliseconds on a forward clock).</summary>
    public string Resolve(string status, long now)
    {
        if (LaptopStatus.IsProblem(status))
        {
            problem = status;
            problemUntil = now + (long)Sticky.TotalMilliseconds;
            return status;
        }

        if (LaptopStatus.IsConnected(status))
        {
            problem = null;
            quiet = null;
            return status;
        }

        if (status != LaptopStatus.Waiting)
        {
            return status;
        }

        if (problem is not null && now < problemUntil)
        {
            return problem;
        }

        return quiet is not null && now < quietUntil ? quiet : status;
    }

    /// <summary>True once per cause per hour for a protocol mismatch: the one refusal the owner cannot see coming.</summary>
    public bool ShouldAnnounce(string status, long now)
    {
        if (!LaptopStatus.IsProblem(status) || status.StartsWith("Refused ", StringComparison.Ordinal) || status.StartsWith("Bluetooth", StringComparison.Ordinal))
        {
            return false;
        }

        if (announced.TryGetValue(status, out var at) && now - at < (long)AnnounceEvery.TotalMilliseconds)
        {
            return false;
        }

        announced[status] = now;
        return true;
    }

    /// <summary>Says <paramref name="text"/> in place of "waiting" until <paramref name="until"/>: connections are being refused on purpose.</summary>
    public void Quiet(string text, long until)
    {
        quiet = text;
        quietUntil = until;
    }

    /// <summary>Forgets what was held, after the owner has dealt with it (a forget, a reset).</summary>
    public void Clear()
    {
        problem = null;
        quiet = null;
    }
}

/// <summary>What the Wi-Fi and USB row has to say when something is wrong; null while all is well.</summary>
internal static class NetworkStatus
{
    public static string? Problem(bool listening, bool announced) =>
        !listening
            ? "Wi-Fi and USB: could not listen. See the log."
            : !announced ? "Wi-Fi and USB: phones may not find this laptop." : null;
}
