namespace Edgepad.Trust;

/// <summary>
/// Keeps network pairing to one at a time, and stops anyone on the network from wearing the owner down with requests.
/// Five pairings in a row that fail (a wrong code typed, a commitment that does not open, a confirmation that does not
/// match) lock pairing for 5 minutes, doubling with every further five, never more than an hour. A prompt the owner
/// cancels or nobody answers in time is not a guess and does not count toward that lock, but it must not be free
/// either, or a host could raise the prompt forever: the host that raised it is refused for a minute, doubling with
/// each further one in a row, never more than an hour, and ten of them in any hour refuse everyone until the oldest
/// is an hour old. A pairing that succeeds clears the count and that host's streak. Counted in memory only, like
/// <see cref="PinStore"/>'s wrong answers: quitting the app needs the desktop.
/// </summary>
internal sealed class PairingLimiter(Func<long>? clock = null)
{
    private const int FailuresPerLock = 5;
    private const int FirstLockSeconds = 300;
    private const int MaxLockSeconds = 3600;
    private const int HostLockSeconds = 60;
    private const int MaxHostLockSeconds = 3600;
    private const int MaxPromptsPerHour = 10;
    private const int MaxTrackedHosts = 64;
    private const long HourMs = 3_600_000;

    private readonly Lock gate = new();

    // Milliseconds, from a clock that only moves forward; a test hands in its own.
    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);

    // When each cancelled or unanswered prompt of the last hour ended, oldest first.
    private readonly Queue<long> prompts = new();

    // Hosts with a streak of cancelled or unanswered prompts. A host is forgotten an hour after its refusal ends.
    private readonly Dictionary<string, HostStreak> hosts = [];

    private bool busy;
    private int failures;
    private int locks;
    private long lockedUntil;

    /// <summary>How many hosts the limiter is remembering, for tests.</summary>
    internal int TrackedHosts
    {
        get
        {
            lock (gate)
            {
                return hosts.Count;
            }
        }
    }

    /// <summary>
    /// Takes the one pairing slot; false while another pairing holds it, pairing is locked, the hour's prompts are
    /// spent, or <paramref name="host"/> is refused. Every true is followed by exactly one <see cref="End"/>.
    /// A null host skips the per-host rule.
    /// </summary>
    public bool TryBegin(string? host = null)
    {
        lock (gate)
        {
            var t = now();
            Expire(t);
            if (busy || t < lockedUntil || prompts.Count >= MaxPromptsPerHour
                || (host is not null && hosts.TryGetValue(host, out var streak) && t < streak.RefusedUntil))
            {
                return false;
            }

            busy = true;
            return true;
        }
    }

    /// <summary>Frees the slot, counting the attempt from <paramref name="host"/> as <paramref name="outcome"/> says.
    /// </summary>
    public void End(PairingOutcome outcome, string? host = null)
    {
        lock (gate)
        {
            busy = false;
            var t = now();
            Expire(t);
            switch (outcome)
            {
                case PairingOutcome.Paired:
                    failures = 0;
                    locks = 0;
                    if (host is not null)
                    {
                        hosts.Remove(host);
                    }

                    break;
                case PairingOutcome.Failed:
                    failures++;
                    if (failures >= FailuresPerLock)
                    {
                        failures = 0;
                        locks++;
                        lockedUntil = t + (LockSeconds(locks) * 1000L);
                    }

                    break;
                case PairingOutcome.Cancelled:
                case PairingOutcome.TimedOut:
                    prompts.Enqueue(t);
                    while (prompts.Count > MaxPromptsPerHour)
                    {
                        prompts.Dequeue();
                    }

                    if (host is not null)
                    {
                        Refuse(host, t);
                    }

                    break;
                case PairingOutcome.Silent:
                    // No prompt was shown, so none of the hour's is spent: else silent requests from many addresses
                    // could refuse everyone without the owner ever seeing one.
                    if (host is not null)
                    {
                        Refuse(host, t);
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private void Refuse(string host, long t)
    {
        if (!hosts.TryGetValue(host, out var streak))
        {
            if (hosts.Count >= MaxTrackedHosts)
            {
                hosts.Remove(hosts.MinBy(h => h.Value.RefusedUntil).Key);
            }

            streak = new HostStreak();
        }

        var count = streak.Count + 1;
        hosts[host] = new HostStreak(count, t + (HostRefusalSeconds(count) * 1000L));
    }

    private void Expire(long t)
    {
        while (prompts.Count > 0 && t >= prompts.Peek() + HourMs)
        {
            prompts.Dequeue();
        }

        foreach (var (host, streak) in hosts)
        {
            if (t >= streak.RefusedUntil + (MaxHostLockSeconds * 1000L))
            {
                hosts.Remove(host);
            }
        }
    }

    // Capped before shifting: 300 << 4 is already past the hour, and a large shift would overflow.
    private static int LockSeconds(int locks) =>
        locks > 4 ? MaxLockSeconds : Math.Min(FirstLockSeconds << (locks - 1), MaxLockSeconds);

    // Capped before shifting, as above: 60 << 6 is already past the hour.
    private static int HostRefusalSeconds(int count) =>
        count > 6 ? MaxHostLockSeconds : Math.Min(HostLockSeconds << (count - 1), MaxHostLockSeconds);

    private readonly record struct HostStreak(int Count, long RefusedUntil);
}

/// <summary>How one pairing attempt ended, which decides whether it counts toward the lock.</summary>
internal enum PairingOutcome
{
    /// <summary>The phone proved the code and is now trusted.</summary>
    Paired,

    /// <summary>Counts toward the lock: a wrong code, a commitment that does not open, a bad confirmation.</summary>
    Failed,

    /// <summary>
    /// The session ended under the prompt, the trusted phones were forgotten, or the laptop could not go on: does not
    /// count.
    /// </summary>
    Abandoned,

    /// <summary>The owner pressed Cancel: refuses that host for a while and spends one of the hour's prompts.</summary>
    Cancelled,

    /// <summary>The owner did not type the code in time, or the phone went silent: as <see cref="Cancelled"/>.
    /// </summary>
    TimedOut,

    /// <summary>The phone went silent before any prompt was shown: refuses that host, spends none of the hour's
    /// prompts.
    /// </summary>
    Silent,
}
