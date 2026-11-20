using System.Collections.Concurrent;
using Edgepad.Bluetooth;
using Edgepad.Dispatch;
using Edgepad.Net;
using Edgepad.Transfer;
using Edgepad.Trust;

namespace Edgepad.Platform;

/// <summary>
/// Serves one phone at a time over whatever link it arrived on, with the Windows hand-off rules
/// (<see cref="Admission{T}"/>). <paramref name="onEnded"/> is told after each session has ended, so the caller can
/// say what is true now.
/// </summary>
internal sealed class SessionHost(
    TrustStore trust,
    PinStore pin,
    Func<string, CancellationToken, bool> askTrust,
    LinkIdentity identity,
    Action<LaptopStatus> onStatus,
    Action onEnded) : IDisposable
{
    // Joined at exit so each session's own cleanup, which releases held keys and buttons, gets to run.
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(2);

    private readonly Admission<LinuxSession> admission = new();

    private readonly Lock gate = new();

    // The admitted session, set under gate in the same step as the admission itself. A transfer socket attaches to this and
    // to nothing else: never to a candidate, which Admission.Sessions would also list.
    private LinuxSession? connected;

    // Network sockets that have not yet said which they are (a phone's HELLO or a transfer socket's BULK_HELLO), with their
    // phone's address. Bounded like TcpLinkListener's unauthenticated sockets, whose count ends once the link layer has
    // proved the phone's key, before this host sees the socket.
    private readonly Dictionary<LinuxSession, string> awaiting = [];

    /// <summary>The connected phone's address, or null.</summary>
    public string? CurrentAddress => admission.CurrentAddress;

    /// <summary>Whether a hold or a quiet period refuses new connections right now; no pairing may start then.</summary>
    public bool IsRefusing => admission.IsRefusing;

    // How the owner reads each phone: its name and the way it came (a Wi-Fi or USB phone's address is a string of hex),
    // as the connected status that names it. Only phones a pairing or a resume let through get here, so it grows with
    // this laptop's own phones, not with whoever opens a socket.
    private readonly ConcurrentDictionary<string, LaptopStatus> shown = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the owner reads for <paramref name="address"/>: the phone's name and the way it came; an address as it is until the phone has reported once.</summary>
    public string Shown(string address) => shown.TryGetValue(address, out var phone) ? phone.PhoneLine : address;

    /// <summary>The connected status for <paramref name="address"/>, "Connected to Pixel · Bluetooth".</summary>
    public LaptopStatus Connected(string address) =>
        shown.TryGetValue(address, out var phone) ? phone : LaptopStatus.Connected(address, PhoneWay.Bluetooth);

    /// <summary>The status line for <paramref name="link"/>'s session, naming the phone as <see cref="Shown"/> does.</summary>
    private Action<LaptopStatus> StatusFor(IPhoneLink link)
    {
        var address = link.Address;
        if (link.Kind == LinkKind.Network)
        {
            // The link does not say Wi-Fi or USB, so neither is claimed.
            PhoneName.Remember(address, link.Label);
            shown[address] = LaptopStatus.Connected(link.Label, PhoneWay.WifiOrUsb);
            return status => onStatus(status.For(link.Label, PhoneWay.WifiOrUsb));
        }

        // The phone's own name, asked here on the session's thread: the D-Bus thread that accepts the link must not wait.
        return status =>
        {
            var name = PhoneName.Of(address) ?? address;
            shown[address] = LaptopStatus.Connected(name, PhoneWay.Bluetooth);
            onStatus(status.For(name, PhoneWay.Bluetooth));
        };
    }

    /// <summary>
    /// Must not block: the session runs on a thread of its own. True when the link was taken; false when it was
    /// refused, and the link is then already released.
    /// </summary>
    public bool Accept(IPhoneLink link)
    {
        var address = link.Address;

        // A device this laptop already knows it will refuse is turned away before it costs a thread. Nothing that
        // gets past this touches the connected phone yet: that waits for the handshake's onAdmitted.
        var refusal = admission.Refusal(address);
        if (refusal is null && trust.Refuses(address))
        {
            refusal = $"Refused {address} without disturbing the connected phone: the owner is being asked about a phone "
                + "or just declined this one, or the trust file cannot be read";
        }

        if (refusal is not null)
        {
            Log.Write(refusal);
            link.Release();
            return false;
        }

        // Input state belongs to one connection: its own virtual keyboard and pad, destroyed when the session ends.
        // The keyboard is made only once the handshake admits the phone, so a refused one never shows a device.
        // Until the session starts, nothing else disposes the platform: a session that never runs never reaches
        // the finally that releases its input, so its worker thread is this method's to end.
        var platform = new LinuxPlatform();
        LinuxSession session;
        try
        {
            session = new LinuxSession(
                link,
                trust,
                pin,
                askTrust,
                new Dispatcher(platform, platform),
                platform.Dispose,
                platform.ProbePad,
                platform.StartReporting,
                () => LinuxPlatform.Shared,
                identity,
                StatusFor(link),
                admitted =>
                {
                    if (!OnAdmitted(admitted))
                    {
                        return false;
                    }

                    platform.Open();
                    return true;
                },
                OnSessionEnded,
                LiveFor);
        }
        catch
        {
            platform.Dispose();
            throw;
        }

        platform.NotifyRequested = session.SetNotify;
        var tooMany = link.Kind == LinkKind.Network && !TryAwait(session, address);
        if (tooMany || !admission.AddCandidate(session, address))
        {
            Log.Write(tooMany ? $"Refused {address}: too many sockets from it are waiting to say what they are" : $"Refused {address}: Edgepad is quitting, or the owner is changing security settings");
            Forget(session);
            session.Dispose();
            platform.Dispose();
            return false;
        }

        session.Start();
        return true;
    }

    // Counted and added under one lock, so two sockets arriving together cannot both take the last place.
    private bool TryAwait(LinuxSession session, string address)
    {
        lock (gate)
        {
            var fromAddress = awaiting.Values.Count(a => string.Equals(a, address, StringComparison.OrdinalIgnoreCase));
            if (!CanAwait(awaiting.Count, fromAddress))
            {
                return false;
            }

            awaiting[session] = address;
            return true;
        }
    }

    private void Forget(LinuxSession session)
    {
        lock (gate)
        {
            _ = awaiting.Remove(session);
        }
    }

    /// <summary>Whether another socket may wait for its first frame: the same limits as the link listener's unauthenticated ones.</summary>
    internal static bool CanAwait(int total, int fromAddress) =>
        total < TcpLinkListener.MaxUnauthenticated && fromAddress < TcpLinkListener.MaxUnauthenticatedPerHost;

    /// <summary>
    /// The session a transfer socket may attach to (<c>hasLiveSession</c>, D-BULK): the one admitted for exactly this socket's
    /// address, still running, and holding no transfer socket yet. Reserved for the caller on the way out. Null otherwise.
    /// This never creates, replaces or admits a session and never calls <see cref="OnAdmitted"/>, and it never reads
    /// <c>Admission.Sessions</c>, which lists candidates too. The socket has said it is a transfer socket, so it stops
    /// counting against the sockets still to identify themselves.
    /// </summary>
    private LinuxSession? LiveFor(LinuxSession bulk)
    {
        LinuxSession? live;
        lock (gate)
        {
            _ = awaiting.Remove(bulk);
            live = connected;
            if (live is null
                || !string.Equals(admission.CurrentAddress, bulk.Address, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(live.Address, bulk.Address, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        // A session that ended is disposed before it is cleared here, and a disposed one refuses.
        return live.ReserveBulk(bulk) ? live : null;
    }

    /// <summary>
    /// Sends a file to the connected phone, on a pool thread. Null when no phone is connected; otherwise how it ended
    /// (<see cref="XferCode.Saved"/> is a file the phone has whole and checked). A file that cannot be read faults the task.
    /// </summary>
    public Task<XferCode?> SendFile(string path, CancellationToken cancel = default)
    {
        LinuxSession? session;
        lock (gate)
        {
            session = connected;
        }

        return session is null
            ? Task.FromResult<XferCode?>(null)
            : Task.Run(() => (XferCode?)session.SendFile(path, cancel), CancellationToken.None);
    }

    /// <summary>Ends every session of the phone at <paramref name="address"/>, the connected one and its candidates.</summary>
    public void Disconnect(string address) => End(admission.Sessions(address));

    /// <summary>
    /// Called by a session once trust and the PIN have said yes, just before it answers HELLO_ACK: the moment a
    /// new phone may replace the connected one.
    /// </summary>
    private bool OnAdmitted(LinuxSession session)
    {
        Forget(session);
        bool admitted;
        LinuxSession? replaced;
        string refusal;
        lock (gate)
        {
            admitted = admission.Admit(session, out replaced, out refusal);
            if (admitted)
            {
                connected = session;
            }
        }

        if (!admitted)
        {
            Log.Write($"Refused {session.Address}: {refusal}");
            return false;
        }

        // Outside the admission lock: the replaced session's exit calls OnSessionEnded.
        if (replaced is not null)
        {
            Log.Write($"{session.Address} replaced the connection with {replaced.Address}");
            replaced.Dispose();
        }

        return true;
    }

    /// <summary>
    /// Hangs up on every phone and refuses new connections until <see cref="Resume"/>. For an owner dialog that
    /// changes security: a connected phone is a keyboard, and a trusted one reconnects within seconds.
    /// </summary>
    public void HoldOff() => End(admission.HoldOff());

    /// <summary>Ends one <see cref="HoldOff"/>.</summary>
    public void Resume() => admission.Resume();

    /// <summary>Hangs up on every phone and refuses new connections for <paramref name="quiet"/>.</summary>
    public void HangUp(TimeSpan quiet) => End(admission.HangUp(quiet));

    // Outside the admission lock: each session's exit calls OnSessionEnded.
    private static void End(List<LinuxSession> sessions)
    {
        foreach (var session in sessions)
        {
            session.Dispose();
        }
    }

    private void OnSessionEnded(LinuxSession session)
    {
        Forget(session);
        lock (gate)
        {
            if (ReferenceEquals(connected, session))
            {
                connected = null;
            }
        }

        admission.Ended(session);
        onEnded();
    }

    public void Dispose()
    {
        // Outside any lock, because each session's exit calls OnSessionEnded. Held keys and buttons are released
        // in the session's own finally on its thread, and the process can exit before that runs.
        var ending = admission.Close();
        End(ending);
        foreach (var session in ending)
        {
            session.Join(ExitGrace);
        }
    }
}
