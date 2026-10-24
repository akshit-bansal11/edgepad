namespace Edgepad.Bluetooth;

/// <summary>
/// Who is connected, who is waiting to be, and who was just taken over from: the Windows server's hand-off rules
/// (<c>windows/src/Edgepad/Bluetooth/RfcommServer.cs</c>) without its transport, so they can be tested. One phone
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

            return HandedOff(address) ? TookOver(address) : null;
        }
    }

    /// <summary>Registers a candidate. False once <see cref="Close"/> ran; the caller then ends it.</summary>
    public bool AddCandidate(T session, string address)
    {
        lock (gate)
        {
            if (closed)
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
            if (closed || !candidates.Remove(session, out var address))
            {
                refusal = "it stopped being a candidate: Edgepad is quitting, or the connection ended meanwhile";
                return false;
            }

            if (HandedOff(address))
            {
                refusal = TookOver(address);
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

    private static string TookOver(string address) =>
        $"Refused {address}: another phone took over this laptop less than {(int)QuietAfterHandOff.TotalSeconds} seconds ago";

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
