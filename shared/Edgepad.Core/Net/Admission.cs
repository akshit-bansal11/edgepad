namespace Edgepad.Net;

/// <summary>
/// Who is connected, who is waiting to be, and who was just taken over from: the hand-off rules both laptops serve
/// phones by, kept apart from any transport so they can be tested. One phone
/// at a time. A new connection is a candidate beside the connected phone and leaves it alone until the handshake
/// has passed trust and the PIN; only then does it replace the old one, because after a dropped link the phone
/// reconnects before the old socket has noticed it is dead. The phone it took over from is refused for 30 s, so
/// two trusted phones cannot go on taking the laptop from each other.
///
/// It only decides. Ending a session is the caller's, outside this class's lock, because a session's own exit
/// comes back here through <see cref="Ended"/>.
/// </summary>
internal sealed class Admission<T>(Func<long>? clock = null)
    where T : class
{
    /// <summary>
    /// How long the phone another one took over from is refused. It outlasts the phone's own automatic retries
    /// (10 attempts, 2 s apart), so the displaced phone stops by itself instead of taking the laptop back.
    /// </summary>
    private static readonly TimeSpan QuietAfterHandOff = TimeSpan.FromSeconds(30);

    private readonly Lock gate = new();

    // Milliseconds, from a clock that only moves forward; a test hands in its own.
    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);

    // Connections not yet admitted, with their phone's address. They touch nothing of the connected phone.
    private readonly Dictionary<T, string> candidates = new(ReferenceEqualityComparer.Instance);

    // Every phone a different one took over from, and until when it is refused. One per address, not just the
    // last: with a single record three trusted phones could still take the laptop round in a circle. Expired
    // entries are pruned on every use, so it cannot grow.
    private readonly Dictionary<string, long> handedOff = new(StringComparer.OrdinalIgnoreCase);

    private T? current;
    private string? currentAddress;
    private bool closed;

    // Owner dialogs that change security (HoldOff/Resume), and the clock reading before which everything is
    // refused after a HangUp; separate, so that ending one cannot end the other. A count rather than a flag: with
    // two dialogs open, closing the first must not let a phone in while the second is still on screen, where a
    // connected phone could answer it.
    private int holds;
    private long refuseUntil;

    /// <summary>The connected phone's address, or null.</summary>
    public string? CurrentAddress
    {
        get
        {
            lock (gate)
            {
                return currentAddress;
            }
        }
    }

    /// <summary>
    /// Why a new connection from <paramref name="address"/> is turned away before it costs a thread, or null when
    /// it may become a candidate.
    /// </summary>
    public string? Refusal(string address)
    {
        lock (gate)
        {
            if (closed)
            {
                return $"Refused {address}: Edgepad is quitting";
            }

            if (Refusing())
            {
                return $"Refused {address}: the owner is changing security settings, or this laptop is forgetting its phones or just forgot them";
            }

            return HandedOff(address) ? $"Refused {address}: {TookOver}" : null;
        }
    }

    /// <summary>
    /// Registers a candidate. False once <see cref="Close"/> ran, or while a hold or quiet period refuses everyone;
    /// the caller then ends it. Checked again here and not only in <see cref="Refusal"/>: a hold that starts
    /// between the two ends every session it can see, and this one is not a candidate yet, so it would otherwise
    /// be added afterwards and ask its trust question over the owner's security dialog.
    /// </summary>
    public bool AddCandidate(T session, string address)
    {
        lock (gate)
        {
            if (closed || Refusing())
            {
                return false;
            }

            candidates[session] = address;
            return true;
        }
    }

    /// <summary>
    /// The handshake's word that <paramref name="session"/> passed trust and the PIN. True makes it the connected
    /// phone; <paramref name="replaced"/> is then the one it took over from, for the caller to end. False when it
    /// is no longer a candidate (ended meanwhile, or Edgepad is quitting), or when its phone was itself just taken
    /// over from: checked again here and not only at accept, because a connection the displaced phone opened just
    /// before would otherwise be admitted now and take the laptop back. <paramref name="refusal"/> says which.
    /// </summary>
    public bool Admit(T session, out T? replaced, out string refusal)
    {
        replaced = null;
        lock (gate)
        {
            if (closed || Refusing() || !candidates.Remove(session, out var address))
            {
                refusal = "it stopped being a candidate: Edgepad is quitting, the owner is changing security settings, or the connection ended meanwhile";
                return false;
            }

            if (HandedOff(address))
            {
                refusal = TookOver;
                return false;
            }

            // The same address is the phone replacing its own stale link after a drop, not a hand-off.
            if (current is not null && !string.Equals(currentAddress, address, StringComparison.OrdinalIgnoreCase))
            {
                handedOff[currentAddress!] = now() + (long)QuietAfterHandOff.TotalMilliseconds;
            }

            replaced = current;
            current = session;
            currentAddress = address;
            refusal = "";
            return true;
        }
    }

    /// <summary>A session ended, by itself or by the caller. True when it was the connected phone.</summary>
    public bool Ended(T session)
    {
        lock (gate)
        {
            candidates.Remove(session);
            if (!ReferenceEquals(current, session))
            {
                return false;
            }

            current = null;
            currentAddress = null;
            return true;
        }
    }

    /// <summary>Every live session, the connected phone's and each candidate's, whose phone is <paramref name="address"/>.</summary>
    public List<T> Sessions(string address)
    {
        lock (gate)
        {
            List<T> found = [.. candidates.Where(c => string.Equals(c.Value, address, StringComparison.OrdinalIgnoreCase)).Select(c => c.Key)];
            if (current is not null && string.Equals(currentAddress, address, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(current);
            }

            return found;
        }
    }

    /// <summary>
    /// Refuses every new connection until <see cref="Resume"/>, and hands back the connected phone and every
    /// candidate for the caller to end. A connected phone is a keyboard: it could answer the owner's dialog.
    /// </summary>
    public List<T> HoldOff()
    {
        lock (gate)
        {
            holds++;
            return TakeSessions();
        }
    }

    /// <summary>Ends one <see cref="HoldOff"/>; a quiet period started by <see cref="HangUp"/> runs on.</summary>
    public void Resume()
    {
        lock (gate)
        {
            holds = Math.Max(0, holds - 1);
        }
    }

    /// <summary>
    /// Refuses every new connection for <paramref name="quiet"/> (never shortening one already running) and hands
    /// back everyone live, for the caller to end.
    /// </summary>
    public List<T> HangUp(TimeSpan quiet)
    {
        lock (gate)
        {
            refuseUntil = Math.Max(refuseUntil, now() + (long)quiet.TotalMilliseconds);
            return TakeSessions();
        }
    }

    /// <summary>Stops admitting anyone, for good, and hands back every live session for the caller to end.</summary>
    public List<T> Close()
    {
        lock (gate)
        {
            closed = true;
            List<T> all = [.. candidates.Keys];
            candidates.Clear();
            if (current is not null)
            {
                all.Add(current);
            }

            return all;
        }
    }

    /// <summary>
    /// The connected phone and every candidate, who stop being candidates, so a trust question answered after this
    /// cannot admit one. Called under the lock.
    /// </summary>
    private List<T> TakeSessions()
    {
        List<T> all = [.. candidates.Keys];
        candidates.Clear();
        if (current is not null)
        {
            all.Add(current);
        }

        return all;
    }

    /// <summary>Whether new connections are being refused, by a hold or a quiet period. Called under the lock.</summary>
    private bool Refusing() => holds > 0 || now() < refuseUntil;

    private static string TookOver =>
        $"another phone took over this laptop less than {(int)QuietAfterHandOff.TotalSeconds} seconds ago";

    /// <summary>Whether <paramref name="address"/> was just taken over from. Called under the lock.</summary>
    private bool HandedOff(string address)
    {
        var time = now();
        foreach (var (phone, until) in handedOff.ToList())
        {
            if (time >= until)
            {
                handedOff.Remove(phone);
            }
        }

        return handedOff.ContainsKey(address);
    }
}
