using Edgepad.Capture;
using Edgepad.Dispatch;
using Edgepad.Macros;
using Edgepad.Net;
using Edgepad.Platform;
using Edgepad.Protocol;
using Edgepad.Trust;

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
    private readonly Func<Action<Frame>, Action<bool>?> startReporting;
    private readonly Func<IReadOnlyList<Macro>> macros;
    private readonly LinkIdentity identity;
    private readonly Action<string> onStatus;
    private readonly Func<LinuxSession, bool> onAdmitted;
    private readonly Action<LinuxSession> onEnded;
    private int disposed;

    // Which icon run may still send: each WANT_ICONS and Dispose move it on, and a run that sees it moved stops.
    private int iconRun;

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
        Func<Action<Frame>, Action<bool>?> startReporting,
        Func<IReadOnlyList<Macro>> macros,
        LinkIdentity identity,
        Action<string> onStatus,
        Func<LinuxSession, bool> onAdmitted,
        Action<LinuxSession> onEnded)
    {
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
        try
        {
            // A refusal comes back here and the finally below closes the socket; a phone gone silent mid-handshake
            // is ended through Dispose, which wakes the blocked read.
            if (!Handshake.Run(frames, Address, trust, pin, askTrust, () => onAdmitted(this), onStatus, Log.Write, Dispose, ended.Token))
            {
                return;
            }

            // On the read loop's thread, like a PONG: a dead socket here should end the session. The opening answer is
            // sent before the phone can ask, so it can hide the pad rather than offer a button that does nothing.
            // The same id the link's INFO reply gives, so a phone sees one laptop over Bluetooth and Wi-Fi.
            frames.Send(new Text((byte)TextKind.Device, identity.DeviceText(DeviceOs.Linux)));
            dispatcher.PadStatusReply = status => frames.Send(new Text((byte)TextKind.PadStatus, status));
            frames.Send(new Text((byte)TextKind.PadStatus, probePad()));
            dispatcher.MacroWatch = startReporting(frames.Send);

            // Captures go to the phone once it asks (SET CAPTURE_TARGET); until then, and after the session, they stay here.
            dispatcher.Capture = new CaptureToPhone(new LinuxCaptureFolders(), frames.Send, () => dispatcher.LastInputMs);

            while (true)
            {
                var frame = frames.ReadFrame();
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
            // Releases anything still held down: Alt in the middle of an app switch, a button mid-drag. Unplugs the
            // pad too, so a phone that drops mid-game does not leave a stick pushed forward.
            releaseHeldInput();
            dispatcher.Capture?.Dispose();
            if (dispatcher.Cadence.Summary() is { } cadence)
            {
                Log.Write($"Pointer frames from {Address} over {link.Kind}: {cadence}");
            }

            if (dispatcher.Dropped > 0)
            {
                Log.Write($"Session with {Address}: {dispatcher.Dropped} frames dropped");
            }

            Dispose();
            onEnded(this);
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
        var current = macros();
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
                    if (!ReferenceEquals(macros(), current) || Volatile.Read(ref iconRun) != run)
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

        ended.Cancel();
        Interlocked.Increment(ref iconRun);

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
