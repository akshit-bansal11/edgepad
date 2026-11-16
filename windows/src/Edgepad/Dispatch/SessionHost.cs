using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Edgepad.Annotation;
using Edgepad.Blob;
using Edgepad.Bluetooth;
using Edgepad.Controls;
using Edgepad.Gamepad;
using Edgepad.Injection;
using Edgepad.Macros;
using Edgepad.Net;
using Edgepad.Transfer;
using Edgepad.Trust;

namespace Edgepad.Dispatch;

/// <summary>
/// Serves one phone at a time over whatever link it arrived on, by the hand-off rules (<see cref="Admission{T}"/>).
/// A new connection starts as a candidate beside the connected phone and leaves it alone until trust says yes; only
/// then does it replace the old one rather than being refused: after a dropped link the phone reconnects before
/// the old socket has noticed it is dead, and refusing would lock the phone out until it did.
/// </summary>
internal sealed class SessionHost(
    Action<LaptopStatus> onStatus,
    TrustStore trust,
    PinStore pin,
    Func<string, CancellationToken, bool> askTrust,
    AudioEndpoint speakers,
    AudioEndpoint microphone,
    BrightnessControl brightness,
    MediaSessions media,
    DisplayModes display,
    MacroStore macros,
    LevelOverlay overlay,
    LinkIdentity identity,
    WindowsClipboard clipboard,
    Func<bool> shareClipboard,
    Action<string> onMacroAdded,
    SynchronizationContext ui,
    Action<Func<bool>> onEnded,
    Action<string>? onFileArrived = null) : IDisposable
{
    // Joined at exit so the session's own cleanup, which releases held keys and buttons, gets to run.
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(2);

    private readonly Admission<Session> admission = new();

    // Taken around an admission and around Close, so the connected phone's thread is never missed by Dispose.
    private readonly Lock gate = new();

    // Every admitted session's thread, the connected one and any replaced one still unwinding, joined at exit; each
    // leaves as its session ends.
    private readonly ConcurrentDictionary<Session, Thread> sessionThreads = new();

    // The admitted session, under gate, set in the same lock as the admission itself. A transfer socket attaches to this
    // and to nothing else: never to a candidate, which Admission.Sessions would also list.
    private Session? connected;

    // Network sockets that have not yet said which they are (a phone's HELLO or a transfer socket's BULK_HELLO), with their
    // phone's address. They are bounded like TcpLinkListener's unauthenticated sockets, whose count ends once the link layer
    // has proved the phone's key, before this host sees the socket.
    private readonly ConcurrentDictionary<Session, string> awaiting = new();

    /// <summary>The connected phone's address, or null.</summary>
    public string? CurrentAddress => admission.CurrentAddress;

    /// <summary>
    /// The connected phone's address when it came over Bluetooth, the only link a restart of the RFCOMM service cuts;
    /// null when no phone is connected or it came over Wi-Fi or USB.
    /// </summary>
    public string? CurrentBluetoothAddress =>
        CurrentAddress is { } live && ShouldDefer(shown.TryGetValue(live, out var phone) ? phone.Way : null) ? live : null;

    /// <summary>Whether a restart of the Bluetooth service waits for a phone connected this way.</summary>
    internal static bool ShouldDefer(PhoneWay? way) => way == PhoneWay.Bluetooth;

    /// <summary>
    /// Whether a session's status reaches the tray. A connected status always does; any other only while no other phone
    /// is the connected one, whose status it would otherwise cover.
    /// </summary>
    internal static bool ShowStatus(string? currentAddress, string address, LaptopStatus status) =>
        status.IsConnected || currentAddress is null || string.Equals(currentAddress, address, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether another socket may wait for its first frame: the same limits as the link listener's unauthenticated ones.</summary>
    internal static bool CanAwait(int total, int fromAddress) =>
        total < TcpLinkListener.MaxUnauthenticated && fromAddress < TcpLinkListener.MaxUnauthenticatedPerHost;

    /// <summary>Whether a hold or a quiet period refuses new connections right now; no pairing may start then.</summary>
    public bool IsRefusing => admission.IsRefusing;

    // How the owner reads each phone: its name, which can arrive after the connection (a Bluetooth phone's is looked up
    // in Windows), and the way it came. Only phones that got as far as a session get here, so it grows with this
    // laptop's own phones, not with whoever opens a socket.
    private readonly ConcurrentDictionary<string, (Func<string> Name, PhoneWay Way)> shown = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the owner reads for <paramref name="address"/>: the phone's name, or its address while it has none.</summary>
    public string Shown(string address) => shown.TryGetValue(address, out var phone) ? phone.Name() : address;

    /// <summary>The connected status for <paramref name="address"/>, "Connected to Pixel · Bluetooth".</summary>
    public LaptopStatus Connected(string address) =>
        shown.TryGetValue(address, out var phone) ? LaptopStatus.Connected(phone.Name(), phone.Way) : LaptopStatus.Connected(address, PhoneWay.Bluetooth);

    /// <summary>The status line for <paramref name="link"/>'s session, naming the phone as <see cref="Shown"/> does.</summary>
    private Action<LaptopStatus> StatusFor(IPhoneLink link)
    {
        var address = link.Address;
        shown[address] = (() => link.Label, link.Kind == LinkKind.Network ? PhoneWay.WifiOrUsb : PhoneWay.Bluetooth);
        return status =>
        {
            if (status.IsConnected)
            {
                onStatus(Connected(address));
                return;
            }

            var named = status.For(Shown(address), PhoneWay.Bluetooth);
            if (ShowStatus(admission.CurrentAddress, address, status))
            {
                onStatus(named);
            }
            else
            {
                Log.Write($"Not shown over the connected phone's status: {named.Text}");
            }
        };
    }

    /// <summary>
    /// Takes a new connection: refuses it, or starts its session as a candidate. Nothing escapes it, and the link
    /// is closed on every path that does not hand it to a session.
    /// </summary>
    public void Accept(IPhoneLink link)
    {
        // Declared out here so the catches can drop a session registered before the failure.
        Session? session = null;

        // A peer that drops in the instant after connecting makes these calls throw.
        try
        {
            var address = link.Address;

            // A device this laptop already knows it will refuse is turned away here, before it costs a thread.
            // Nothing that gets past this touches the connected phone yet: a new session only replaces it once
            // trust has said yes (OnAdmitted), because trust must not be handed to something that has not proved
            // it speaks this protocol, and anything less would let any bonded device drop the owner's link at
            // will just by connecting, over and over.
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
                return;
            }

            // Input state (what is held down) belongs to one connection; the devices are shared. The virtual
            // controller belongs to the connection for the same reason and one more: it is a device plugged
            // into Windows for as long as the phone that asked for it is there, and no longer.
            var injector = new InputInjector();
            var pad = new VirtualPad();

            // The annotation layer too: strokes are one phone's, and the layer goes when that phone does. It is
            // drawn on the UI thread, which a connection's thread is not, so it is handed the tray's context.
            var annotator = new Annotator(ui);
            var platform = new WindowsPlatform(injector, pad, speakers, microphone, brightness, media, display, overlay, annotator);
            var dispatcher = new Dispatcher(platform, macros);
            session = new Session(
                link, trust, pin, askTrust, injector, pad, dispatcher, annotator, speakers, microphone, brightness, media, display, macros, identity, clipboard, shareClipboard, onMacroAdded, StatusFor(link), OnAdmitted, OnSessionEnded, LiveFor, path => onFileArrived?.Invoke(path));

            // A dedicated thread, not the thread pool: the read loop blocks for the life of the connection,
            // and input is injected from this thread directly, so nothing queues behind other work.
            var thread = new Thread(session.Run) { IsBackground = true, Name = "edgepad-session", Priority = ThreadPriority.AboveNormal };

            // Tested again here: a HangUp that ran since the check above has already looked for sessions to end,
            // and would have missed this one.
            var tooMany = link.Kind == LinkKind.Network && !TryAwait(session, address);
            if (tooMany || !admission.AddCandidate(session, address))
            {
                // Too many sockets waiting to say what they are, or a hold, a quiet period or quitting started between the
                // check above and here.
                Log.Write(tooMany ? $"Refused {address}: too many sockets from it are waiting to say what they are" : admission.Refusal(address) ?? $"Refused {address}: connections are being held off");
                _ = awaiting.TryRemove(session, out _);
                session.Dispose();
                injector.Dispose();
                pad.Dispose();
                annotator.Dispose();
                return;
            }

            thread.Start();
        }
        catch (Exception e) when (e is COMException or ObjectDisposedException or InvalidOperationException)
        {
            Log.Write($"A connection was dropped before it could be served: {e.Message}");
            Drop(link, session);
        }
        catch (Exception e)
        {
            // Top-level boundary of a connection's arrival: nothing may escape it, and the link must not leak.
            Log.Write($"A connection could not be served: {e}");
            Drop(link, session);
        }
    }

    /// <summary>
    /// Closes a connection that failed before its session could run. A session registered but never started
    /// would otherwise stay a candidate, and nothing would end it.
    /// </summary>
    private void Drop(IPhoneLink link, Session? session)
    {
        link.Release();
        if (session is not null)
        {
            _ = awaiting.TryRemove(session, out _);
            admission.Ended(session);
        }
    }

    // Counted and added under one lock, so two sockets arriving together cannot both take the last place.
    private bool TryAwait(Session session, string address)
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

    /// <summary>
    /// The session a transfer socket may attach to (<c>hasLiveSession</c>, D-BULK): the one admitted for exactly this socket's
    /// address, still running, and holding no transfer socket yet. Reserved for the caller on the way out. Null otherwise.
    /// This never creates, replaces or admits a session and never calls <see cref="OnAdmitted"/>. The socket has said it is a
    /// transfer socket, so it stops counting against the sockets still to identify themselves.
    /// </summary>
    private Session? LiveFor(Session bulk)
    {
        _ = awaiting.TryRemove(bulk, out _);
        Session? live;
        lock (gate)
        {
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
        Session? session;
        lock (gate)
        {
            session = connected;
        }

        return session is null
            ? Task.FromResult<XferCode?>(null)
            : Task.Run(() => (XferCode?)session.SendFile(path, cancel), CancellationToken.None);
    }

    /// <summary>
    /// Called by a session once trust has said yes, just before it answers HELLO_ACK: the moment a new phone
    /// may replace the connected one. False when connections stopped being taken while the owner was asked, or
    /// the session was ended meanwhile; the session then ends without answering.
    /// </summary>
    private bool OnAdmitted(Session session)
    {
        _ = awaiting.TryRemove(session, out _);
        bool admitted;
        Session? previous;
        string refusal;
        lock (gate)
        {
            admitted = admission.Admit(session, out previous, out refusal);
            if (admitted)
            {
                connected = session;
                // The handshake asks from the session's own thread, the one Dispose waits for.
                sessionThreads[session] = Thread.CurrentThread;
            }
        }

        if (!admitted)
        {
            Log.Write($"Refused {session.Address}: {refusal}");
            return false;
        }

        // Outside the lock, like every other Dispose here: the replaced session's exit calls OnSessionEnded.
        if (previous is not null)
        {
            Log.Write($"{session.Address} replaced the connection with {previous.Address}");
            previous.Dispose();
        }

        return true;
    }

    /// <summary>
    /// Hands the session's ending to the caller, to run under its own lock: true when it was the connected phone.
    /// Whatever the caller does for that phone's end (its status, a restart it put off for it) then changes
    /// together with the connected phone.
    /// </summary>
    private void OnSessionEnded(Session session)
    {
        sessionThreads.TryRemove(session, out _);
        _ = awaiting.TryRemove(session, out _);
        lock (gate)
        {
            if (ReferenceEquals(connected, session))
            {
                connected = null;
            }
        }

        onEnded(() => admission.Ended(session));
    }

    /// <summary>
    /// Hangs up on the connected phone and refuses every new connection for <paramref name="quiet"/>, never
    /// shortening a quiet period already running. Forgetting a phone that stayed connected would otherwise
    /// change nothing until it next reconnected, and a phone just forgotten would reconnect by itself and
    /// raise the trust question under the owner's cursor. A connection still waiting to be admitted is ended too.
    /// </summary>
    public void HangUp(TimeSpan quiet) => End(admission.HangUp(quiet));

    /// <summary>
    /// Hangs up on the connected phone and refuses every new connection until <see cref="Resume"/>. A phone
    /// that is still trusted reconnects within seconds, so hanging up alone would not keep it out while the
    /// owner decides. A connection still waiting to be admitted is ended too.
    /// </summary>
    public void HoldOff() => End(admission.HoldOff());

    /// <summary>
    /// Ends one <see cref="HoldOff"/>; connections are refused until every hold has ended. A quiet period started
    /// by <see cref="HangUp"/> runs on.
    /// </summary>
    public void Resume() => admission.Resume();

    // Outside the admission lock. Not cleared here: each session's own exit does that, and posts the status when
    // it does.
    private static void End(List<Session> sessions)
    {
        foreach (var session in sessions)
        {
            session.Dispose();
        }
    }

    public void Dispose()
    {
        List<Session> ending;
        List<Thread> threads;
        lock (gate)
        {
            ending = admission.Close();
            threads = [.. sessionThreads.Values];
        }

        // Outside the lock, because each session's exit calls OnSessionEnded. The injected keys and buttons are
        // released in Session.Run's finally on those background threads, and the process can exit before it runs,
        // leaving Alt or a mouse button held down in Windows. One budget for all of them, not one each.
        End(ending);
        var waited = Stopwatch.StartNew();
        foreach (var thread in threads)
        {
            var left = ExitGrace - waited.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                break;
            }

            if (thread.IsAlive)
            {
                thread.Join(left);
            }
        }
    }
}
