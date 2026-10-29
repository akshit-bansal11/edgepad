namespace Edgepad.Trust;

/// <summary>
/// Keeps network pairing to one at a time, and stops anyone on the network from wearing the owner down with requests.
/// Five pairings in a row that fail (a wrong code typed, a commitment that does not open, a phone that does not
/// confirm) lock pairing for 5 minutes, doubling with every further five, never more than an hour. The owner
/// cancelling does not count: that is the owner deciding, not anyone guessing. A pairing that succeeds clears the
/// count. Counted in memory only, like <see cref="PinStore"/>'s wrong answers: quitting the app needs the desktop.
/// </summary>
internal sealed class PairingLimiter(Func<long>? clock = null)
{
    private const int FailuresPerLock = 5;
    private const int FirstLockSeconds = 300;
    private const int MaxLockSeconds = 3600;

    private readonly Lock gate = new();

    // Milliseconds, from a clock that only moves forward; a test hands in its own.
    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);

    private bool busy;
    private int failures;
    private int locks;
    private long lockedUntil;

    /// <summary>
    /// Takes the one pairing slot; false while another pairing holds it or pairing is locked. Every true is followed by
    /// exactly one <see cref="End"/>.
    /// </summary>
    public bool TryBegin()
    {
        lock (gate)
        {
            if (busy || now() < lockedUntil)
            {
                return false;
            }

            busy = true;
            return true;
        }
    }

    /// <summary>Frees the slot, counting the attempt as <paramref name="outcome"/> says.</summary>
    public void End(PairingOutcome outcome)
    {
        lock (gate)
        {
            busy = false;
            switch (outcome)
            {
                case PairingOutcome.Paired:
                    failures = 0;
                    locks = 0;
                    break;
                case PairingOutcome.Failed:
                    failures++;
                    if (failures >= FailuresPerLock)
                    {
                        failures = 0;
                        locks++;
                        lockedUntil = now() + (LockSeconds(locks) * 1000L);
                    }

                    break;
                default:
                    break;
            }
        }
    }

    // Capped before shifting: 300 << 4 is already past the hour, and a large shift would overflow.
    private static int LockSeconds(int locks) =>
        locks > 4 ? MaxLockSeconds : Math.Min(FirstLockSeconds << (locks - 1), MaxLockSeconds);
}

internal enum PairingOutcome
{
    Paired,

    /// <summary>Counts toward the lock.</summary>
    Failed,

    /// <summary>The owner cancelled, the session ended, or the laptop could not go on: does not count.</summary>
    Abandoned,
}
