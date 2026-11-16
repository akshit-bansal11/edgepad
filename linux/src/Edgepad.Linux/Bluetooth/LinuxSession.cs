using Edgepad.Blob;
using Edgepad.Capture;
using Edgepad.Dispatch;
using Edgepad.Foreground;
using Edgepad.Macros;
using Edgepad.Media;
using Edgepad.Net;
using Edgepad.Platform;
using Edgepad.Power;
using Edgepad.Protocol;
using Edgepad.Transfer;
using Edgepad.Trust;
using Edgepad.Ui;

namespace Edgepad.Bluetooth;

/// <summary>
/// One connected phone over its link: the shared handshake (trust, asked through
/// askTrust, then the PIN, then the server's word through onAdmitted), then a blocking read loop on its own thread
/// that turns each frame straight into input, as the Windows session does. The platform reports state (volume,
/// brightness, media, macros) through the sink the session hands it after the handshake, from threads of its own.
/// </summary>
internal sealed class LinuxSession : IDisposable
{
    private readonly IPhoneLink link;
    private readonly FrameStream frames;
    private readonly Thread thread;
    private readonly TrustStore trust;
    private readonly PinStore pin;
    private readonly Func<string, CancellationToken, bool> askTrust;
    private readonly Dispatcher dispatcher;
    private readonly Action releaseHeldInput;
    private readonly Func<string> probePad;
    private readonly Func<Action<Frame>, Func<long>, Action<bool>?> startReporting;
    private readonly Func<MacroStore> macros;
    private readonly LinkIdentity identity;
    private readonly Action<LaptopStatus> onStatus;
    private readonly Func<LinuxSession, bool> onAdmitted;
    private readonly Action<LinuxSession> onEnded;
    private readonly Func<LinuxSession, LinuxSession?> liveFor;
    private int disposed;
    private Action<bool>? inputState;
    private Action<MediaSnapshot>? mediaObserver;
    private PowerSource? powerSource;
    private PowerReporter? power;
    private ForegroundReporter? foreground;
    private volatile NotificationMonitor? notifications;

    // Which icon run may still send: each WANT_ICONS and Dispose move it on, and a run that sees it moved stops.
    private int iconRun;

    // Files. The receiver and sender are the control session's; a transfer socket (a LinuxSession of its own, see RunBulk)
    // only reads XFER frames into them and carries what they send while it is attached.
    private volatile TransferReceiver? receiver;
    private volatile TransferSender? fileSender;
    private int transferId;

    // The transfer socket this session holds, or is about to: set when it asks, cleared when it ends. Under bulkGate.
    private readonly Lock bulkGate = new();
    private LinuxSession? bulkOwner;

    // On a transfer socket's own session: HELLO_ACK went out, so the live session may send on it.
    private volatile bool bulkLive;

    // Cancelled by Dispose, so a trust question open for this session, or about to be put, is withdrawn.
    private readonly CancellationTokenSource ended = new();

    /// <summary>
    /// Takes ownership of <paramref name="link"/>. <paramref name="releaseHeldInput"/> releases every key and
    /// button the phone still holds down when the session ends, and unplugs its virtual controller.
    /// <paramref name="probePad"/> answers the PAD_STATUS token sent once after the handshake, and
    /// <paramref name="startReporting"/> is given the sink the platform sends its state reports through and answers
    /// with the macro apps' watch, if this desktop has one, and
    /// <paramref name="macros"/> is read each time the phone asks for the macro icons.
    /// </summary>
    public LinuxSession(
        IPhoneLink link,
        TrustStore trust,
        PinStore pin,
        Func<string, CancellationToken, bool> askTrust,
        Dispatcher dispatcher,
        Action releaseHeldInput,
        Func<string> probePad,
        Func<Action<Frame>, Func<long>, Action<bool>?> startReporting,
        Func<MacroStore> macros,
        LinkIdentity identity,
        Action<LaptopStatus> onStatus,
        Func<LinuxSession, bool> onAdmitted,
        Action<LinuxSession> onEnded,
        Func<LinuxSession, LinuxSession?> liveFor)
    {
        this.liveFor = liveFor;
        this.identity = identity;
        this.link = link;
        Address = link.Address;
        this.trust = trust;
        this.pin = pin;
        this.askTrust = askTrust;
        this.dispatcher = dispatcher;
        this.releaseHeldInput = releaseHeldInput;
        this.probePad = probePad;
        this.startReporting = startReporting;
        this.macros = macros;
        this.onStatus = onStatus;
        this.onAdmitted = onAdmitted;
        this.onEnded = onEnded;
        frames = new FrameStream(link.Input, link.Output);

        // A dedicated thread, not the thread pool: the read loop blocks for the life of the connection, and
        // input is injected from this thread directly, so nothing queues behind other work.
        thread = new Thread(Run) { IsBackground = true, Name = "edgepad-session", Priority = ThreadPriority.AboveNormal };
    }

    /// <summary>The phone's address, as the log and the trust file name it.</summary>
    public string Address { get; }

    public void Start() => thread.Start();

    /// <summary>The phone's SET NOTIFY. Nothing before the handshake: the monitor does not exist yet.</summary>
    public void SetNotify(bool on) => notifications?.SetPhone(on);

    /// <summary>Waits up to <paramref name="timeout"/> for the read loop's own cleanup, which releases held input.</summary>
    public void Join(TimeSpan timeout)
    {
        if (thread.IsAlive)
        {
            thread.Join(timeout);
        }
    }

    private void Run()
    {
        LinuxSession? live = null;
        try
        {
            // A refusal comes back here and the finally below closes the socket; a phone gone silent mid-handshake
            // is ended through Dispose, which wakes the blocked read. A transfer socket (BULK_HELLO) is answered without
            // trust, PIN or onAdmitted and attaches only to the connected phone's live session, which liveFor finds and reserves.
            var outcome = Handshake.Open(
                frames,
                Address,
                trust,
                pin,
                askTrust,
                () => onAdmitted(this),
                onStatus,
                Log.Write,
                Dispose,
                ended.Token,
                hasLiveSession: () => (live = liveFor(this)) is not null,
                linkKind: link.Kind);
            if (outcome == HandshakeOutcome.Refused)
            {
                return;
            }

            if (outcome == HandshakeOutcome.Bulk)
            {
                RunBulk(live!);
                return;
            }

            // On the read loop's thread, like a PONG: a dead socket here should end the session. The opening answer is
            // sent before the phone can ask, so it can hide the pad rather than offer a button that does nothing.
            // The same id the link's INFO reply gives, so a phone sees one laptop over Bluetooth and Wi-Fi.
            frames.Send(new Text((byte)TextKind.Device, identity.DeviceText(DeviceOs.Linux)));

            // TEXT 18: whether this laptop may create input devices, now and on every change while connected.
            // Check raises Changed only on a flip, on the probing thread; a send to a dead socket is ignored. Subscribed
            // before the first answer, so a flip between the two is not lost.
            inputState = SendInputState;
            UinputAccess.Changed += inputState;
            var inputBlocked = UinputAccess.Check() is not null;
            frames.Send(new Text((byte)TextKind.InputState, UinputAccess.StateText(inputBlocked)));

            // The same word feeds TEXT 24 (capture needs the device), which the annotator sends with TEXT 9.
            Annotator.InputBlocked(inputBlocked);

            // TEXT 23, best effort: only the recordings this phone started are seen.
            dispatcher.CaptureStateReply = state => frames.Send(new Text((byte)TextKind.CaptureState, state));

            // Sleep or shut down when the media ends, only where logind answers (the same test gates the feature token).
            // Fed by the laptop's media poll; that poll is shared, so a dead socket here must not end its thread.
            if (LinuxPower.Reachable)
            {
                var end = new EndAction(LinuxPower.Perform, token => frames.Send(new Text((byte)TextKind.EndAction, token)));
                dispatcher.EndAction = end;
                mediaObserver = snapshot =>
                {
                    try
                    {
                        end.Observe(snapshot);
                    }
                    catch (Exception e) when (e is IOException or ObjectDisposedException)
                    {
                        // This session's socket is gone; the read loop ends it.
                    }
                };
                StateReporter.MediaObserved += mediaObserver;
            }

            // Files, both ways, one at a time each. A part is kept when the session ends, for the next BEGIN of the same file.
            var incoming = new TransferReceiver(
                LinuxTransfers.Destination(), LinuxTransfers.PartialFolder(), status => Guarded(() => SendTransfer(status)), LinuxTransfers.Arrived);
            dispatcher.Transfers = incoming;
            receiver = incoming;
            var outgoing = new TransferSender(SendTransfer, link.Kind, () => dispatcher.LastInputMs, ended.Token);
            dispatcher.Sending = outgoing;
            fileSender = outgoing;

            dispatcher.PadStatusReply = status => frames.Send(new Text((byte)TextKind.PadStatus, status));
            frames.Send(new Text((byte)TextKind.PadStatus, probePad()));
            dispatcher.MacroWatch = startReporting(frames.Send, () => dispatcher.LastInputMs);

            // TEXT 21: the app in front, from CONTROL 11. Wayland cannot see it, so the control stays unset there and is dropped and counted.
            foreground = new ForegroundReporter(new LinuxForeground(), text => frames.Send(new Text((byte)TextKind.ForegroundApp, text)));
            dispatcher.ForegroundWatch = LinuxForeground.Offered ? foreground.Watch : null;

            // TEXT 19/20: the batteries every 60 s, the default output every 3 s, each sent only when it changed. The
            // reporter reads on its own timers, so a slow UPower or sound tool never holds up this read loop.
            powerSource = new PowerSource();
            power = new PowerReporter(powerSource.ReadBatteries, powerSource.ReadOutput, frames.Send, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(3));
            power.Start();

            // The shared clipboard: moves nothing until the phone asks on this link, and stops at once when the tray
            // switch goes off. Polled, since there is no change event to listen to.
            var surface = new LinuxClipboard(
                Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
                Environment.GetEnvironmentVariable("DISPLAY"));

            // Captures go to the phone once it asks (SET CAPTURE_TARGET); until then, and after the session, they stay here.
            // A shot that only reached the clipboard is found through the same tool, while a capture is being waited for.
            dispatcher.Capture = new CaptureToPhone(
                new LinuxCaptureFolders(surface.Fingerprint, surface.Read, () => surface.Available), frames.Send, () => dispatcher.LastInputMs);
            var sender = new BlobSender(frames.Send, link.Kind, () => dispatcher.LastInputMs, ended.Token);
            var sync = new ClipboardSync(new QuietClipboard(surface), sender, () => ClipboardSetting.Enabled, ended.Token);
            dispatcher.Clipboard = sync;
            _ = Task.Run(() => LinuxClipboard.Poll(surface.Fingerprint, sync, () => ClipboardSetting.Enabled, ended.Token));

            // Notification forwarding: no bus connection until the tray switch is on and the phone sent SET NOTIFY 1.
            notifications = new NotificationMonitor((label, data) => sender.Send(BlobKind.Notification, label, data));

            // The apps the phone may pick from: a pick is a token into a list built here, never a path from the phone.
            dispatcher.Apps = new AppPicker(new DesktopEntryCatalog(), macros(), sender, ended.Token);
            dispatcher.AppAddReply = status => frames.Send(new Text((byte)TextKind.AppAddStatus, status));

            // A vanished peer holds the blocked read for minutes; Dispose from the timer's thread closes the link and wakes it.
            using var silence = new SilenceWatch(SilenceWatch.PhoneSilence, () => { Log.Write($"Session with {Address} ended: no frame for 30 s"); Dispose(); }, every: TimeSpan.FromSeconds(5));

            while (true)
            {
                var frame = frames.ReadFrame();
                silence.Touch();
                if (frame is Ping ping)
                {
                    frames.Send(new Pong(ping.Time));
                }
                else if (frame is Text { Kind: (byte)TextKind.WantIcons } want)
                {
                    // Answered here rather than in the dispatcher, for the same reason PONG is: the reply goes back
                    // down this socket, and the dispatcher holds no reference to it.
                    StartIcons(MacroIconChunks.Snap(want.Value, LinuxMacroIcons.MaxSize));
                }
                else
                {
                    dispatcher.Handle(frame);
                }
            }
        }
        // The HELLO timeout, a replacing phone and the server quitting all shut the socket down under a blocked
        // read, which then ends the stream or finds the stream disposed.
        catch (Exception e) when (e is IOException or InvalidDataException or ObjectDisposedException or OperationCanceledException)
        {
            Log.Write($"Session with {Address} ended: {e.Message}");
        }
        // Anything else is a bug on this side. It ends this one session, with the whole exception in the log,
        // rather than the app.
        catch (Exception e)
        {
            Log.Write($"Session with {Address} failed: {e}");
        }
        finally
        {
            // A transfer socket gives its place back to the session it was attached to, ended or never attached.
            live?.ReleaseBulk(this);

            // Releases anything still held down: Alt in the middle of an app switch, a button mid-drag. Unplugs the
            // pad too, so a phone that drops mid-game does not leave a stick pushed forward.
            // Each step nested, so one that throws still lets the rest run: held input is released and the server
            // told the session ended whatever happens before it.
            try
            {
                if (inputState is { } handler)
                {
                    UinputAccess.Changed -= handler;
                }

                if (mediaObserver is { } observer)
                {
                    StateReporter.MediaObserved -= observer;
                }

                // Held input first: a reporter that is slow to stop must never leave keys down. The reporter no
                // longer holds its lock across a blocked send, so stopping it after cannot hang.
                releaseHeldInput();
            }
            finally
            {
                try
                {
                    // The reporter before its source, so no new timer read starts once the source is closed. A read
                    // already under way runs outside the reporter's lock, so it can still finish against the closed source.
                    foreground?.Dispose();
                    try
                    {
                        power?.Dispose();
                    }
                    finally
                    {
                        powerSource?.Dispose();
                    }

                    notifications?.Dispose();
                    receiver?.Dispose();
                    dispatcher.Capture?.Dispose();
                    if (dispatcher.Cadence.Summary() is { } cadence)
                    {
                        Log.Write($"Pointer frames from {Address} over {link.Kind}: {cadence}");
                    }

                    if (dispatcher.Dropped > 0)
                    {
                        Log.Write($"Session with {Address}: {dispatcher.Dropped} frames dropped");
                    }
                }
                finally
                {
                    try
                    {
                        Dispose();
                    }
                    finally
                    {
                        onEnded(this);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A transfer socket's whole life after HELLO_ACK: it reads only file-transfer frames, hands them to the session it is
    /// attached to, and ends on anything else (a repeated BULK_HELLO included) or after 30 s without a frame.
    /// </summary>
    private void RunBulk(LinuxSession live)
    {
        bulkLive = true;
        using var silence = new SilenceWatch(
            Handshake.BulkSilenceLimit,
            () =>
            {
                Log.Write($"Transfer socket of {Address} closed: no frame for 30 s");
                Dispose();
            },
            every: TimeSpan.FromSeconds(5));
        while (true)
        {
            var frame = frames.ReadFrame();
            silence.Touch();
            if (!Handshake.IsLegalOnBulk(frame))
            {
                Log.Write($"Transfer socket of {Address} closed: it sent a frame that is not part of a file transfer");
                return;
            }

            live.TakeTransfer(frame);
        }
    }

    /// <summary>Reserves this session's one transfer socket for <paramref name="bulk"/>; false when it already has one or has ended.</summary>
    internal bool ReserveBulk(LinuxSession bulk)
    {
        lock (bulkGate)
        {
            if (Volatile.Read(ref disposed) != 0 || bulkOwner is not null)
            {
                return false;
            }

            bulkOwner = bulk;
            return true;
        }
    }

    internal void ReleaseBulk(LinuxSession bulk)
    {
        lock (bulkGate)
        {
            if (ReferenceEquals(bulkOwner, bulk))
            {
                bulkOwner = null;
            }
        }
    }

    /// <summary>A file-transfer frame from either socket. Only these four reach here, and the receiver and sender hold their own locks.</summary>
    internal void TakeTransfer(Frame frame) => _ = frame switch
    {
        XferBegin f => receiver?.Take(f) ?? false,
        XferChunk f => receiver?.Take(f) ?? false,
        XferEnd f => receiver?.Take(f) ?? false,
        XferStatus f => (fileSender?.Take(f) ?? false) | (receiver?.Take(f) ?? false),
        _ => false,
    };

    /// <summary>Sends <paramref name="path"/> to the phone and blocks until it ends; never call it on the read thread.</summary>
    internal XferCode SendFile(string path, CancellationToken cancel)
    {
        if (fileSender is not { } current)
        {
            return XferCode.SessionEnded;
        }

        var id = (byte)((uint)Interlocked.Increment(ref transferId) % 255 + 1);
        return current.Send(path, id, cancel);
    }

    // Everything this laptop says about a file goes down the transfer socket while one is attached, else the control one.
    private void SendTransfer(Frame frame)
    {
        LinuxSession? bulk;
        lock (bulkGate)
        {
            bulk = bulkOwner;
        }

        (bulk is { bulkLive: true } ? bulk.frames : frames).Send(frame);
    }

    private static void Guarded(Action send)
    {
        try
        {
            send();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The read loop notices the dead socket and ends the session; a lost status costs nothing.
        }
    }

    private void SendInputState(bool blocked)
    {
        try
        {
            frames.Send(new Text((byte)TextKind.InputState, UinputAccess.StateText(blocked)));
            Annotator.InputBlocked(blocked);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // The socket is gone; the read loop ends the session.
        }
    }

    /// <summary>
    /// Sends the icons from a pool thread, never the read loop: the phone asks as soon as it connects and whenever
    /// the list changes, so a macro tapped then would otherwise wait behind every icon's rendering. A newer
    /// WANT_ICONS supersedes this run, and Dispose ends it. Not the platform's worker thread: a long icon run would
    /// hold up the volume and mute jobs queued behind it.
    /// </summary>
    private void StartIcons(int size)
    {
        var run = Interlocked.Increment(ref iconRun);
        _ = Task.Run(() =>
        {
            // Nothing may escape a pool thread.
            try
            {
                SendIcons(run, size);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // The read loop notices the dead socket and ends the session; a lost icon costs nothing.
            }
            catch (Exception e)
            {
                Log.Write($"Macro icons for {Address} failed: {e}");
            }
        });
    }

    /// <summary>
    /// Every macro icon this laptop can read, at <paramref name="size"/> and in pieces, for as long as
    /// <paramref name="run"/> is the current run. An icon that cannot be read is not sent, and its button keeps its name.
    /// </summary>
    private void SendIcons(int run, int size)
    {
        var current = macros().Macros;
        for (var slot = 0; slot < current.Count; slot++)
        {
            // Before each render too, so a superseded run stops reading icons nobody will be sent.
            if (Volatile.Read(ref iconRun) != run)
            {
                return;
            }

            if (LinuxMacroIcons.Png(current[slot], size) is not { } png)
            {
                continue;
            }

            foreach (var chunk in MacroIconChunks.Chunks(slot, png))
            {
                // The list can be reloaded while this runs, which swaps it. Checked under the send lock, no chunk of
                // the old list can follow the new one to the phone, where it would land on whatever macro moved into
                // its slot. The phone asks again for the new list. Checking the run there too keeps a superseded run
                // from interleaving with the one after it, and a disposed session from writing at all: Dispose moves
                // the run on before closing anything.
                lock (frames.SendGate)
                {
                    if (!ReferenceEquals(macros().Macros, current) || Volatile.Read(ref iconRun) != run)
                    {
                        return;
                    }

                    frames.Send(new Text((byte)TextKind.MacroIcon, chunk));
                }
            }
        }
    }

    public void Dispose()
    {
        // Reached twice for a session the server replaced: once by the server, once by its own read loop.
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        LinuxSession? transferSocket;
        lock (bulkGate)
        {
            transferSocket = bulkOwner;
            bulkOwner = null;
        }

        ended.Cancel();
        Interlocked.Increment(ref iconRun);

        // The transfer socket goes with the session it belongs to.
        transferSocket?.Dispose();

        // Closed first, and outside the send lock: a Send blocked in a write to a phone that has stopped
        // reading holds that lock for as long as the write blocks. Closing the link fails that write and
        // ends the blocked read, and only then are the streams disposed, never under a writer's feet.
        link.Close();

        lock (frames.SendGate)
        {
            link.Release();
        }

        // Already cancelled, so a question still reading the token sees that, never a live one.
        ended.Dispose();
    }
}
