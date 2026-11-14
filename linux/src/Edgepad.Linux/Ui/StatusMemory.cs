using Edgepad.Net;

namespace Edgepad.Ui;

/// <summary>
/// What the tray should say, given the statuses that arrive. A refusal is posted and then overwritten with "Waiting
/// for your phone" the moment the refused session ends, so on its own it would be on screen for an instant; this
/// keeps it until a phone connects or ten minutes pass. It also decides when a refusal earns a notification: once
/// per distinct cause per hour. This duplicates the Windows app's StatusMemory and belongs in the shared core.
/// </summary>
internal sealed class StatusMemory
{
    public static readonly TimeSpan Sticky = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan AnnounceEvery = TimeSpan.FromHours(1);

    private readonly Dictionary<LaptopStatus, long> announced = [];
    private LaptopStatus? problem;
    private long problemUntil;

    /// <summary>The status to show for <paramref name="status"/> at <paramref name="now"/> (milliseconds on a forward clock).</summary>
    public LaptopStatus Resolve(LaptopStatus status, long now)
    {
        if (status.IsProblem)
        {
            problem = status;
            problemUntil = now + (long)Sticky.TotalMilliseconds;
            return status;
        }

        if (status.IsConnected)
        {
            problem = null;
            return status;
        }

        return status.Kind == StatusKind.Waiting && problem is not null && now < problemUntil ? problem : status;
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

    /// <summary>Forgets what was held, after the owner has dealt with it (a forget).</summary>
    public void Clear() => problem = null;
}
