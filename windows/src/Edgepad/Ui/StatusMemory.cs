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

    private readonly Dictionary<LaptopStatus, long> announced = [];
    private LaptopStatus? problem;
    private long problemUntil;
    private LaptopStatus? quiet;
    private long quietUntil;

    /// <summary>The text to show for <paramref name="status"/> at <paramref name="now"/> (milliseconds on a forward clock).</summary>
    public LaptopStatus Resolve(LaptopStatus status, long now)
    {
        // A radio condition is shown while it is true and not remembered: the "waiting" that follows means it recovered.
        if (status.IsRadioCondition)
        {
            return status;
        }

        if (status.IsProblem)
        {
            problem = status;
            problemUntil = now + (long)Sticky.TotalMilliseconds;
            return status;
        }

        if (status.IsConnected)
        {
            problem = null;
            quiet = null;
            return status;
        }

        if (status.Kind != StatusKind.Waiting)
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
    public bool ShouldAnnounce(LaptopStatus status, long now)
    {
        if (!status.IsMismatch)
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
    public void Quiet(LaptopStatus text, long until)
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
            ? Strings.Network_CouldNotListen
            : !announced ? Strings.Network_PhonesMayNotFind : null;
}
