using System.Runtime.InteropServices;
using Edgepad.Annotation;
using Edgepad.Controls;
using Edgepad.Dispatch;
using Edgepad.Gamepad;
using Edgepad.Injection;
using Edgepad.Macros;
using Edgepad.Protocol;
using Edgepad.Trust;
using Microsoft.Win32;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Radios;
using Windows.Networking.Sockets;

namespace Edgepad.Bluetooth;

/// <summary>
/// Advertises the Edgepad RFCOMM service and serves one phone at a time. A new connection starts as a candidate
/// beside the connected phone and leaves it alone until trust says yes; only then does it replace the old one
/// rather than being refused: after a dropped link the phone reconnects before the old socket has noticed it is
/// dead, and refusing would lock the phone out until it did. The service is started again by itself when a start
/// fails, when the laptop wakes from sleep and when the Bluetooth radio comes back on, any of which used to leave
/// it deaf while the tray still said it was waiting.
/// </summary>
internal sealed class RfcommServer(
    Action<string> onStatus,
    TrustStore trust,
    PinStore pin,
    Func<string, bool> askTrust,
    AudioEndpoint speakers,
    AudioEndpoint microphone,
    BrightnessControl brightness,
    MediaSessions media,
    DisplayModes display,
    MacroStore macros,
    LevelOverlay overlay,
    SynchronizationContext ui) : IDisposable
{
    // Joined at exit so the session's own cleanup, which releases held keys and buttons, gets to run.
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the phone another one took over from is refused. It outlasts the phone's own automatic retries
    /// (10 attempts, 2 s apart), so the displaced phone stops by itself instead of taking the laptop back, and two
    /// trusted phones cannot go on taking it from each other.
    /// </summary>
    private static readonly TimeSpan QuietAfterHandOff = TimeSpan.FromSeconds(30);

    /// <summary>How long a failed start waits before the next try, for as long as it takes.</summary>
    private static readonly TimeSpan RetryStartEvery = TimeSpan.FromSeconds(30);

    private const string Waiting = "Waiting for your phone";
    private const string BluetoothOff = "Bluetooth is off";
    private const string StartFailing = "Bluetooth is off or unavailable; trying again";

    private readonly Lock gate = new();

    // Connections that have not yet been admitted, with the threads they run on. They are not the connected phone
    // and touch nothing of it, but HangUp, HoldOff and Dispose still have to be able to end them.
    private readonly Dictionary<Session, Thread> candidates = new();

    private RfcommServiceProvider? provider;
    private StreamSocketListener? listener;
    private Session? current;
    private Thread? sessionThread;

    // Environment.TickCount64 milliseconds before which every new connection is refused; see HangUp.
    private long refuseUntil;

    // How many owner dialogs (Forget, Security) are open; separate from the quiet period so that ending one
    // cannot end the other. A count rather than a flag: with two dialogs open, closing the first must not let a
    // phone in while the second is still on screen, where a connected phone could answer it.
    private int holds;

    // Every phone a different one took over from, and until when (Environment.TickCount64) it is refused. One per
    // address, not just the last: with a single record three trusted phones could still take the laptop round in
    // a circle. Expired entries are pruned on every use, so it cannot grow.
    private readonly Dictionary<string, long> handedOff = new(StringComparer.OrdinalIgnoreCase);

    // Set while a start, or the wait before retrying one, is under way. There is only ever one; a restart asked
    // for meanwhile is folded into it through startAgain.
    private bool starting;
    private string? startAgain;

    // Completed to cut the wait before the next try short, when a restart is asked for during it.
    private TaskCompletionSource? wake;

    // A restart asked for while a phone was connected. It runs when that phone's session ends: tearing the
    // service down under a working link would cost the owner the link for nothing.
    private string? deferredRestart;

    // The radio whose state is watched, and whether it last said it was off.
    private Radio? radio;
    private bool radioOff;

    private bool disposed;

    // The last failure logged, so a radio that stays off is not reported every 30 s. Only the start under way
    // touches these, and there is only ever one.
    private string? lastStartFailure;
    private string? lastRadioProblem;

    /// <summary>
    /// Starts the service, trying again every 30 s until it is up, and from then on restarts it after sleep and
    /// whenever the radio comes back on. Called once, on the UI thread, which is where the power events are
    /// raised from then on.
    /// </summary>
    public Task StartAsync()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        lock (gate)
        {
            starting = true;
        }

        return StartUntilServingAsync(null);
    }

    /// <summary>
    /// Starts the service, and keeps trying until it is up or the server is disposed. A non-null
    /// <paramref name="restart"/> says why the running service is being replaced. Only one runs at a time:
    /// whoever sets the starting flag under the lock runs it.
    /// </summary>
    private async Task StartUntilServingAsync(string? restart)
    {
        try
        {
            while (true)
            {
                var woken = Task.CompletedTask;
                string? deferredFor = null;
                lock (gate)
                {
                    if (disposed)
                    {
                        starting = false;
                        return;
                    }

                    // A phone admitted since the restart was asked for keeps its link; the restart waits for it.
                    if (restart is not null && current is { } live)
                    {
                        deferredRestart = restart;
                        deferredFor = live.Address;
                        starting = false;
                    }
                    else
                    {
                        startAgain = null;
                        wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        woken = wake.Task;
                    }
                }

                if (deferredFor is not null)
                {
                    Log.Write($"Restarting the Bluetooth service once {deferredFor} disconnects: {restart}");
                    return;
                }

                if (restart is not null)
                {
                    Log.Write($"Restarting the Bluetooth service: {restart}");
                    StopServing();
                }

                try
                {
                    await StartOnceAsync();
                }
                catch (Exception e)
                {
                    // Bluetooth not ready at sign-in, a radio switched off, an adapter unplugged: all of them a
                    // state to wait out, not an end. Logged once per distinct reason.
                    if (e.Message != lastStartFailure)
                    {
                        lastStartFailure = e.Message;
                        Log.Write($"Bluetooth service failed to start; trying again every {(int)RetryStartEvery.TotalSeconds} s: {e}");
                    }

                    lock (gate)
                    {
                        if (!disposed && current is null)
                        {
                            onStatus(StartFailing);
                        }
                    }

                    await Task.WhenAny(woken, Task.Delay(RetryStartEvery));

                    // Nothing is running to be replaced: the next round is a plain start.
                    restart = null;
                    continue;
                }

                lastStartFailure = null;
                Log.Write("RFCOMM service advertising");
                await WatchRadioAsync();
                lock (gate)
                {
                    if (disposed)
                    {
                        starting = false;
                        return;
                    }

                    onStatus(StatusNow());
                    if (startAgain is null)
                    {
                        starting = false;
                        return;
                    }

                    // Asked for while this start was under way, so it may have started on a radio that was
                    // still coming up.
                    restart = startAgain;
                }
            }
        }
        catch (Exception e)
        {
            // Top-level boundary of a fire-and-forget task: an escaped exception here would be lost silently.
            Log.Write($"The Bluetooth service stopped trying to start: {e}");
            lock (gate)
            {
                starting = false;
            }
        }
    }

    /// <summary>
    /// One start. A half-finished one is undone before the exception leaves, so the next try starts clean, and
    /// so is one that <see cref="Dispose"/> overtook.
    /// </summary>
    private async Task StartOnceAsync()
    {
        var socketListener = new StreamSocketListener();
        RfcommServiceProvider? serviceProvider = null;
        var advertising = false;
        var published = false;
        try
        {
            serviceProvider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(ProtocolConstants.ServiceId));
            socketListener.ConnectionReceived += OnConnectionReceived;

            // Encryption with authentication: only a device already paired with this laptop can connect.
            await socketListener.BindServiceNameAsync(
                serviceProvider.ServiceId.AsString(),
                SocketProtectionLevel.BluetoothEncryptionWithAuthentication);

            serviceProvider.StartAdvertising(socketListener);
            advertising = true;
            lock (gate)
            {
                published = !disposed;
                if (published)
                {
                    provider = serviceProvider;
                    listener = socketListener;
                }
            }
        }
        finally
        {
            if (!published)
            {
                if (advertising)
                {
                    StopAdvertising(serviceProvider);
                }

                socketListener.Dispose();
            }
        }
    }

    /// <summary>Stops advertising and closes the listener. Connections already taken are left to run.</summary>
    private void StopServing()
    {
        RfcommServiceProvider? stopping;
        StreamSocketListener? closing;
        lock (gate)
        {
            stopping = provider;
            closing = listener;
            provider = null;
            listener = null;
        }

        StopAdvertising(stopping);
        closing?.Dispose();
    }

    private static void StopAdvertising(RfcommServiceProvider? stopping)
    {
        try
        {
            stopping?.StopAdvertising();
        }
        catch (Exception e) when (e is InvalidOperationException or COMException)
        {
            // Already stopped, or the radio went away: nothing left to stop. Throwing here left a stuck
            // process holding the single-instance lock, so no update could take over.
            Log.Write($"StopAdvertising: {e.Message}");
        }
    }

    /// <summary>
    /// Restarts the service, now or once the connected phone's session ends, folded into a start already under
    /// way if there is one.
    /// </summary>
    private void RequestRestart(string reason)
    {
        string? deferredFor = null;
        var begin = false;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (current is { } live)
            {
                deferredRestart = reason;
                deferredFor = live.Address;
            }
            else if (starting)
            {
                startAgain = reason;
                wake?.TrySetResult();
            }
            else
            {
                starting = true;
                begin = true;
            }
        }

        if (deferredFor is not null)
        {
            Log.Write($"Restarting the Bluetooth service once {deferredFor} disconnects: {reason}");
        }
        else if (begin)
        {
            _ = StartUntilServingAsync(reason);
        }
    }

    /// <summary>
    /// Watches the Bluetooth radio found now, in place of any watched before: it may not have existed at sign-in,
    /// and the one watched before may be gone. Without one, a failed start is still retried; this only makes a
    /// radio switched back on take effect at once.
    /// </summary>
    private async Task WatchRadioAsync()
    {
        try
        {
            var found = (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (found is null)
            {
                NoteRadioProblem("No Bluetooth radio was found to watch; a failed start is still retried every 30 s");
                return;
            }

            var off = found.State != RadioState.On;
            found.StateChanged += OnRadioStateChanged;
            Radio? previous = null;
            bool kept;
            lock (gate)
            {
                kept = !disposed;
                if (kept)
                {
                    previous = radio;
                    radio = found;
                    radioOff = off;
                }
            }

            // Subscribed before the old one is let go, so no change falls between the two; one subscription
            // either way once this is done.
            if (!kept)
            {
                found.StateChanged -= OnRadioStateChanged;
            }

            if (previous is not null)
            {
                previous.StateChanged -= OnRadioStateChanged;
            }

            lastRadioProblem = null;
        }
        catch (Exception e)
        {
            NoteRadioProblem($"The Bluetooth radio could not be watched; a failed start is still retried every 30 s: {e.Message}");
        }
    }

    private void NoteRadioProblem(string problem)
    {
        if (problem != lastRadioProblem)
        {
            lastRadioProblem = problem;
            Log.Write(problem);
        }
    }

    private void OnRadioStateChanged(Radio sender, object? args)
    {
        // Top-level boundary of a WinRT event: an exception out of it would be lost, and the radio is the one
        // device here most likely to vanish mid-call.
        try
        {
            var off = sender.State != RadioState.On;
            bool changed;
            lock (gate)
            {
                changed = !disposed && off != radioOff;
                if (changed)
                {
                    radioOff = off;
                    if (off)
                    {
                        onStatus(BluetoothOff);
                    }
                }
            }

            if (!changed)
            {
                return;
            }

            if (off)
            {
                Log.Write("The Bluetooth radio turned off");
            }
            else
            {
                RequestRestart("the Bluetooth radio turned back on");
            }
        }
        catch (Exception e)
        {
            Log.Write($"The Bluetooth radio's state could not be read: {e}");
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            RequestRestart("this laptop resumed from sleep");
        }
    }

    /// <summary>
    /// Hangs up on the connected phone and refuses every new connection for <paramref name="quiet"/>, never
    /// shortening a quiet period already running. Forgetting a phone that stayed connected would otherwise
    /// change nothing until it next reconnected, and a phone just forgotten would reconnect by itself and
    /// raise the trust question under the owner's cursor. A connection still waiting to be admitted is ended too.
    /// </summary>
    public void HangUp(TimeSpan quiet)
    {
        List<Session> ending;
        lock (gate)
        {
            refuseUntil = Math.Max(refuseUntil, Environment.TickCount64 + (long)quiet.TotalMilliseconds);
            ending = TakeSessions();
        }

        // Not cleared here: each session's own exit does that, and posts the status when it does.
        foreach (var session in ending)
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// Hangs up on the connected phone and refuses every new connection until <see cref="Resume"/>. A phone
    /// that is still trusted reconnects within seconds, so hanging up alone would not keep it out while the
    /// owner decides. A connection still waiting to be admitted is ended too.
    /// </summary>
    public void HoldOff()
    {
        List<Session> ending;
        lock (gate)
        {
            holds++;
            ending = TakeSessions();
        }

        // Not cleared here: each session's own exit does that, and posts the status when it does.
        foreach (var session in ending)
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// Ends one <see cref="HoldOff"/>; connections are refused until every hold has ended. A quiet period started
    /// by <see cref="HangUp"/> runs on.
    /// </summary>
    public void Resume()
    {
        lock (gate)
        {
            holds = Math.Max(0, holds - 1);
        }
    }

    /// <summary>
    /// The connected phone's session and every candidate's, for the caller to end. The candidates stop being
    /// candidates here, so a trust question answered after this cannot admit one. Called under the lock.
    /// </summary>
    private List<Session> TakeSessions()
    {
        List<Session> all = [.. candidates.Keys];
        candidates.Clear();
        if (current is not null)
        {
            all.Add(current);
        }

        return all;
    }

    /// <summary>Whether new connections are being refused, by a hold or a quiet period. Called under the lock.</summary>
    private bool Refusing() => holds > 0 || Environment.TickCount64 < refuseUntil;

    /// <summary>Whether <paramref name="address"/> was just taken over from. Called under the lock.</summary>
    private bool HandedOff(string address)
    {
        PruneHandOffs();
        return handedOff.ContainsKey(address);
    }

    /// <summary>Drops the hand-offs whose quiet period is over. Called under the lock.</summary>
    private void PruneHandOffs()
    {
        var now = Environment.TickCount64;
        foreach (var (address, until) in handedOff.ToList())
        {
            if (now >= until)
            {
                handedOff.Remove(address);
            }
        }
    }

    private static string TookOver(string address) =>
        $"Refused {address}: another phone took over this laptop less than {(int)QuietAfterHandOff.TotalSeconds} seconds ago";

    /// <summary>
    /// What the tray says when nothing more particular is going on: the connected phone, a radio that is off, or
    /// waiting. The "Connected to " prefix is what the tray's badge reads. Called under the lock.
    /// </summary>
    private string StatusNow()
    {
        if (current is { } live)
        {
            return $"Connected to {live.Address}";
        }

        return radioOff ? BluetoothOff : Waiting;
    }

    private void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        // Declared out here so the catches can dispose the socket and drop a session registered before the failure.
        StreamSocket? socket = null;
        Session? session = null;

        // A peer that drops in the instant after connecting makes these calls throw, and an exception out of
        // a WinRT event handler would leak the socket.
        try
        {
            socket = args.Socket;
            var address = socket.Information.RemoteHostName.RawName;

            // A device this laptop already knows it will refuse is turned away here, before it costs a thread.
            // Nothing that gets past this touches the connected phone yet: a new session only replaces it once
            // trust has said yes (OnAdmitted), because trust must not be handed to something that has not proved
            // it speaks this protocol, and anything less would let any bonded device drop the owner's link at
            // will just by connecting, over and over.
            string? refusal = null;
            lock (gate)
            {
                if (Refusing())
                {
                    refusal = Forgetting(address);
                }
                else if (HandedOff(address))
                {
                    refusal = TookOver(address);
                }
            }

            if (refusal is null && trust.Refuses(address))
            {
                refusal = $"Refused {address} without disturbing the connected phone: the owner is being asked about a phone "
                    + "or just declined this one, or the trust file cannot be read";
            }

            if (refusal is not null)
            {
                Log.Write(refusal);
                socket.Dispose();
                return;
            }

            // Input state (what is held down) belongs to one connection; the devices are shared. The virtual
            // controller belongs to the connection for the same reason and one more: it is a device plugged
            // into Windows for as long as the phone that asked for it is there, and no longer.
            var injector = new InputInjector();
            var pad = new VirtualPad();

            // The annotation layer too: strokes are one phone's, and the layer goes when that phone does. It is
            // drawn on the UI thread, which this event's thread is not, so it is handed the tray's context.
            var annotator = new Annotator(ui);
            var dispatcher = new Dispatcher(injector, pad, speakers, microphone, brightness, media, display, macros, overlay, annotator);
            session = new Session(
                socket, trust, pin, askTrust, injector, pad, dispatcher, annotator, speakers, microphone, brightness, media, display, macros, onStatus, OnAdmitted, OnSessionEnded);

            // A dedicated thread, not the thread pool: the read loop blocks for the life of the connection,
            // and input is injected from this thread directly, so nothing queues behind other work.
            var thread = new Thread(session.Run) { IsBackground = true, Name = "edgepad-session", Priority = ThreadPriority.AboveNormal };
            bool quiet;
            lock (gate)
            {
                // Tested again here: a HangUp that ran since the check above has already looked for sessions
                // to end, and would have missed this one.
                quiet = disposed || Refusing();
                if (!quiet)
                {
                    candidates.Add(session, thread);
                }
            }

            if (quiet)
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
            Drop(socket, session);
        }
        catch (Exception e)
        {
            // Top-level boundary of a WinRT event: nothing may escape it, and the socket must not leak.
            Log.Write($"A connection could not be served: {e}");
            Drop(socket, session);
        }
    }

    /// <summary>
    /// Closes a connection that failed before its session could run. A session registered but never started
    /// would otherwise stay a candidate, and nothing would end it.
    /// </summary>
    private void Drop(StreamSocket? socket, Session? session)
    {
        socket?.Dispose();
        if (session is not null)
        {
            lock (gate)
            {
                candidates.Remove(session);
            }
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
        Session? previous;
        bool tookOver;
        lock (gate)
        {
            if (!candidates.Remove(session, out var thread) || disposed || Refusing())
            {
                return false;
            }

            // Checked again here, not only at accept: a connection the displaced phone opened just before it was
            // taken over from would otherwise be admitted now and take the laptop back.
            tookOver = HandedOff(session.Address);
            previous = tookOver ? null : current;
            if (!tookOver)
            {
                current = session;
                sessionThread = thread;

                // The same address is the phone replacing its own stale link after a drop, not a hand-off.
                if (previous is not null && !string.Equals(previous.Address, session.Address, StringComparison.OrdinalIgnoreCase))
                {
                    handedOff[previous.Address] = Environment.TickCount64 + (long)QuietAfterHandOff.TotalMilliseconds;
                }
            }
        }

        if (tookOver)
        {
            Log.Write(TookOver(session.Address));
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

    private void OnSessionEnded(Session session)
    {
        string? restart = null;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            candidates.Remove(session);
            if (current == session)
            {
                current = null;
                restart = deferredRestart;
                deferredRestart = null;
            }

            // Every ending says what is true now. A candidate's question or refusal must not stay over a live
            // phone's status, and a phone that was replaced may have posted its own status after the one that
            // replaced it. Posted under the lock, so it cannot overtake a newer one.
            onStatus(StatusNow());
        }

        if (restart is not null)
        {
            RequestRestart(restart);
        }
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        RfcommServiceProvider? stopping;
        StreamSocketListener? closing;
        List<Session> ending;
        Thread? thread;
        Radio? watched;
        lock (gate)
        {
            disposed = true;
            wake?.TrySetResult();
            stopping = provider;
            closing = listener;
            provider = null;
            listener = null;
            ending = TakeSessions();
            thread = sessionThread;
            current = null;
            watched = radio;
            radio = null;
        }

        StopAdvertising(stopping);
        closing?.Dispose();

        // Outside the lock, because each session's exit calls OnSessionEnded, which takes it. The injected keys
        // and buttons are released in Session.Run's finally on that background thread, and the process can
        // exit before it runs, leaving Alt or a mouse button held down in Windows.
        foreach (var session in ending)
        {
            session.Dispose();
        }

        if (thread is { IsAlive: true })
        {
            thread.Join(ExitGrace);
        }

        try
        {
            if (watched is not null)
            {
                watched.StateChanged -= OnRadioStateChanged;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or COMException)
        {
            // The radio went away with its subscription; there is nothing left to unsubscribe from.
            Log.Write($"Unwatching the Bluetooth radio: {e.Message}");
        }
    }
}
