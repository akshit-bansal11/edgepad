using Edgepad.Capture;
using Edgepad.Dispatch;
using Edgepad.Macros;
using Edgepad.Native;
using Edgepad.Net;
using Edgepad.Platform;
using Edgepad.Protocol;
using Edgepad.Trust;
using Microsoft.Win32.SafeHandles;

namespace Edgepad.Bluetooth;

/// <summary>
/// One connected phone over the RFCOMM socket BlueZ handed over: the shared handshake (trust, asked through
/// askTrust, then the PIN, then the server's word through onAdmitted), then a blocking read loop on its own thread
/// that turns each frame straight into input, as the Windows session does. The platform reports state (volume,
/// brightness, media, macros) through the sink the session hands it after the handshake, from threads of its own.
/// </summary>
internal sealed class LinuxSession : IDisposable
{
    private readonly SafeFileHandle socket;
    private readonly FileStream reader;
    private readonly FileStream writer;
    private readonly FrameStream frames;
    private readonly Thread thread;
    private readonly TrustStore trust;
    private readonly PinStore pin;
    private readonly Func<string, CancellationToken, bool> askTrust;
    private readonly Dispatcher dispatcher;
    private readonly Action releaseHeldInput;
    private readonly Func<string> probePad;
    private readonly Action<Action<Frame>> startReporting;
    private readonly Func<IReadOnlyList<Macro>> macros;
    private readonly Action<string> onStatus;
    private readonly Func<LinuxSession, bool> onAdmitted;
    private readonly Action<LinuxSession> onEnded;
    private int disposed;

    // Cancelled by Dispose, so a trust question open for this session, or about to be put, is withdrawn.
    private readonly CancellationTokenSource ended = new();

    /// <summary>
    /// Takes ownership of <paramref name="socket"/>. <paramref name="releaseHeldInput"/> releases every key and
    /// button the phone still holds down when the session ends, and unplugs its virtual controller.
    /// <paramref name="probePad"/> answers the PAD_STATUS token sent once after the handshake, and
    /// <paramref name="startReporting"/> is given the sink the platform sends its state reports through, and
    /// <paramref name="macros"/> is read each time the phone asks for the macro icons.
    /// </summary>
    public LinuxSession(
        SafeFileHandle socket,
        string address,
        TrustStore trust,
        PinStore pin,
        Func<string, CancellationToken, bool> askTrust,
        Dispatcher dispatcher,
        Action releaseHeldInput,
        Func<string> probePad,
        Action<Action<Frame>> startReporting,
        Func<IReadOnlyList<Macro>> macros,
        Action<string> onStatus,
        Func<LinuxSession, bool> onAdmitted,
        Action<LinuxSession> onEnded)
    {
        this.socket = socket;
        Address = address;
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

        Libc.MakeBlocking(socket);

        // A FileStream over the socket's descriptor: on Linux it reads and writes a handle that cannot seek with
        // plain read(2) and write(2), which is all an RFCOMM stream socket needs. The alternative, a Socket over a
        // SafeSocketHandle, asks the kernel for the socket's address family on construction, and .NET has no
        // mapping for AF_BLUETOOTH. UNVERIFIED on a real link (docs/LINUX.md); if it fails there, the fallback is
        // `new NetworkStream(new Socket(new SafeSocketHandle(fd, ownsHandle: true)), ownsSocket: true)`.
        //
        // Two streams over the one handle, because a FileStream is not safe for a read on one thread and a write
        // on another. The reader is buffered (one read(2) returns whatever the phone has sent, never waiting to
        // fill the buffer); the writer is not, so a frame leaves when Send writes it. Both dispose the same
        // SafeHandle, which closes the descriptor once.
        reader = new FileStream(socket, FileAccess.Read, bufferSize: 4096);
        writer = new FileStream(socket, FileAccess.Write, bufferSize: 0);
        frames = new FrameStream(reader, writer);

        // A dedicated thread, not the thread pool: the read loop blocks for the life of the connection, and
        // input is injected from this thread directly, so nothing queues behind other work.
        thread = new Thread(Run) { IsBackground = true, Name = "edgepad-session", Priority = ThreadPriority.AboveNormal };
    }

    /// <summary>The phone's Bluetooth address, as the log and the trust file name it.</summary>
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
            dispatcher.PadStatusReply = status => frames.Send(new Text((byte)TextKind.PadStatus, status));
            frames.Send(new Text((byte)TextKind.PadStatus, probePad()));
            startReporting(frames.Send);

            // Captures go to the phone once it asks (SET CAPTURE_TARGET); until then, and after the session, they stay here.
            dispatcher.Capture = new CaptureToPhone(new LinuxCaptureFolders(), frames.Send, () => dispatcher.LastInputMs);

            while (true)
            {
                var frame = frames.ReadFrame();
                if (frame is Ping ping)
                {
                    frames.Send(new Pong(ping.Time));
                }
                else if (frame is Text { Kind: (byte)TextKind.WantIcons })
                {
                    // Answered here rather than in the dispatcher, for the same reason PONG is: the reply goes back
                    // down this socket, and the dispatcher holds no reference to it.
                    SendIcons();
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
            if (dispatcher.Dropped > 0)
            {
                Log.Write($"Session with {Address}: {dispatcher.Dropped} frames dropped");
            }

            Dispose();
            onEnded(this);
        }
    }

    /// <summary>
    /// Every macro icon this laptop can read, in pieces. Runs on the read loop, so no input frame is read while it
    /// goes out. The phone asks when it opens the macro grid, a screen with no trackpad on it, so the pause falls
    /// where nothing is being pointed at. An icon that cannot be read is not sent, and its button keeps its name.
    /// </summary>
    private void SendIcons()
    {
        var current = macros();
        for (var slot = 0; slot < current.Count; slot++)
        {
            if (LinuxMacroIcons.Png(current[slot]) is not { } png)
            {
                continue;
            }

            foreach (var chunk in MacroIconChunks.Chunks(slot, png))
            {
                // The list can be reloaded while this runs, which swaps it. Checked under the send lock, no chunk of
                // the old list can follow the new one to the phone, where it would land on whatever macro moved into
                // its slot. The phone asks again for the new list.
                lock (frames.SendGate)
                {
                    if (!ReferenceEquals(macros(), current))
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

        // Shut down first, and outside the send lock: a Send blocked in a write to a phone that has stopped
        // reading holds that lock for as long as the write blocks. Shutting the socket down fails that write and
        // ends the blocked read, and only then are the streams disposed, never under a writer's feet.
        try
        {
            Libc.Shutdown(socket);
        }
        catch (ObjectDisposedException)
        {
            // Already closed: nothing left to wake.
        }

        lock (frames.SendGate)
        {
            writer.Dispose();
            reader.Dispose();
        }

        // Already cancelled, so a question still reading the token sees that, never a live one.
        ended.Dispose();

        // Both streams closed it already; said again so the handle is plainly this class's to close.
        socket.Dispose();
    }
}
