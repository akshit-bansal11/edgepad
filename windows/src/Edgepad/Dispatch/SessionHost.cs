using System.Runtime.InteropServices;
using Edgepad.Annotation;
using Edgepad.Bluetooth;
using Edgepad.Controls;
using Edgepad.Gamepad;
using Edgepad.Injection;
using Edgepad.Macros;
using Edgepad.Net;
using Edgepad.Trust;

namespace Edgepad.Dispatch;

/// <summary>
/// Serves one phone at a time over whatever link it arrived on, by the hand-off rules (<see cref="Admission{T}"/>).
/// A new connection starts as a candidate beside the connected phone and leaves it alone until trust says yes; only
/// then does it replace the old one rather than being refused: after a dropped link the phone reconnects before
/// the old socket has noticed it is dead, and refusing would lock the phone out until it did.
/// </summary>
internal sealed class SessionHost(
    Action<string> onStatus,
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
    SynchronizationContext ui,
    Action<Func<bool>> onEnded) : IDisposable
{
    // Joined at exit so the session's own cleanup, which releases held keys and buttons, gets to run.
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(2);

    private readonly Admission<Session> admission = new();

    // Taken around an admission and around Close, so the connected phone's thread is never missed by Dispose.
    private readonly Lock gate = new();

    // The thread the connected phone's session runs on, joined at exit.
    private Thread? sessionThread;

    /// <summary>The connected phone's address, or null.</summary>
    public string? CurrentAddress => admission.CurrentAddress;

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
                link.Close();
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
                link, trust, pin, askTrust, injector, pad, dispatcher, annotator, speakers, microphone, brightness, media, display, macros, onStatus, OnAdmitted, OnSessionEnded);

            // A dedicated thread, not the thread pool: the read loop blocks for the life of the connection,
            // and input is injected from this thread directly, so nothing queues behind other work.
            var thread = new Thread(session.Run) { IsBackground = true, Name = "edgepad-session", Priority = ThreadPriority.AboveNormal };

            // Tested again here: a HangUp that ran since the check above has already looked for sessions to end,
            // and would have missed this one.
            if (!admission.AddCandidate(session, address))
            {
                // HangUp ran between the check above and here; this connection is one it meant to refuse.
                Log.Write(Forgetting(address));
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
        link.Close();
        if (session is not null)
        {
            admission.Ended(session);
        }
    }

    private static string Forgetting(string address) =>
        $"Refused {address}: this laptop is forgetting its phones, or just forgot them";

    /// <summary>
    /// Called by a session once trust has said yes, just before it answers HELLO_ACK: the moment a new phone
    /// may replace the connected one. False when connections stopped being taken while the owner was asked, or
    /// the session was ended meanwhile; the session then ends without answering.
    /// </summary>
    private bool OnAdmitted(Session session)
    {
        bool admitted;
        Session? previous;
        string refusal;
        lock (gate)
        {
            admitted = admission.Admit(session, out previous, out refusal);
            if (admitted)
            {
                // The handshake asks from the session's own thread, the one Dispose waits for.
                sessionThread = Thread.CurrentThread;
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
    private void OnSessionEnded(Session session) => onEnded(() => admission.Ended(session));

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
        Thread? thread;
        lock (gate)
        {
            ending = admission.Close();
            thread = sessionThread;
        }

        // Outside the lock, because each session's exit calls OnSessionEnded. The injected keys and buttons are
        // released in Session.Run's finally on that background thread, and the process can exit before it runs,
        // leaving Alt or a mouse button held down in Windows.
        End(ending);
        if (thread is { IsAlive: true })
        {
            thread.Join(ExitGrace);
        }
    }
}
