using System.Runtime.InteropServices;
using Edgepad.Controls;
using Edgepad.Dispatch;
using Edgepad.Gamepad;
using Edgepad.Injection;
using Edgepad.Macros;
using Edgepad.Protocol;
using Edgepad.Trust;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace Edgepad.Bluetooth;

/// <summary>
/// Advertises the Edgepad RFCOMM service and serves one phone at a time. A new connection replaces the
/// old one rather than being refused: after a dropped link the phone reconnects before the old socket
/// has noticed it is dead, and refusing would lock the phone out until it did.
/// </summary>
internal sealed class RfcommServer(
    Action<string> onStatus,
    TrustStore trust,
    Func<string, bool> askTrust,
    AudioEndpoint speakers,
    AudioEndpoint microphone,
    BrightnessControl brightness,
    MediaSessions media,
    DisplayModes display,
    MacroStore macros,
    LevelOverlay overlay) : IDisposable
{
    // Joined at exit so the session's own cleanup, which releases held keys and buttons, gets to run.
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(2);

    private readonly Lock gate = new();
    private RfcommServiceProvider? provider;
    private StreamSocketListener? listener;
    private Session? current;
    private Thread? sessionThread;

    // Environment.TickCount64 milliseconds before which every new connection is refused; see HangUp.
    private long refuseUntil;

    public async Task StartAsync()
    {
        provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(ProtocolConstants.ServiceId));
        listener = new StreamSocketListener();
        listener.ConnectionReceived += OnConnectionReceived;

        // Encryption with authentication: only a device already paired with this laptop can connect.
        await listener.BindServiceNameAsync(
            provider.ServiceId.AsString(),
            SocketProtectionLevel.BluetoothEncryptionWithAuthentication);

        provider.StartAdvertising(listener);
        Log.Write("RFCOMM service advertising");
        onStatus("Waiting for your phone");
    }

    /// <summary>
    /// Hangs up on the connected phone and refuses every new connection for <paramref name="quiet"/>. Forgetting
    /// a phone that stayed connected would otherwise change nothing until it next reconnected, and a phone just
    /// forgotten would reconnect by itself and raise the trust question under the owner's cursor.
    /// </summary>
    public void HangUp(TimeSpan quiet)
    {
        Session? live;
        lock (gate)
        {
            live = current;
            refuseUntil = Environment.TickCount64 + (long)quiet.TotalMilliseconds;
        }

        // Not cleared here: the session's own exit does that, and posts the waiting status when it does.
        live?.Dispose();
    }

    private void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        // A peer that drops in the instant after connecting makes these calls throw, and an exception out of
        // a WinRT event handler would leak the socket.
        try
        {
            // A device this laptop already knows it will refuse must never get far enough to replace the phone
            // that is connected. The check that grants trust stays on the session thread, after HELLO, because
            // trust on first use must not be handed to something that has not proved it speaks this protocol —
            // but that check runs *after* the new session has displaced the old one, so without this any other
            // bonded device could drop the owner's link at will just by connecting, over and over.
            //
            // A different trusted address refuses here, and so does a trust file that cannot be read, since the
            // session would refuse it anyway. Only nothing trusted yet goes on to the session, where the owner is
            // asked about it.
            var address = args.Socket.Information.RemoteHostName.RawName;
            long quietUntil;
            lock (gate)
            {
                quietUntil = refuseUntil;
            }

            if (Environment.TickCount64 < quietUntil)
            {
                Log.Write($"Refused {address}: this laptop was told to forget its phone a moment ago");
                args.Socket.Dispose();
                return;
            }

            if (trust.Refuses(address))
            {
                Log.Write($"Refused {address} without disturbing the connected phone: it is not the trusted phone, or the owner is being asked about one or just declined it");
                args.Socket.Dispose();
                return;
            }

            // Input state (what is held down) belongs to one connection; the devices are shared. The virtual
            // controller belongs to the connection for the same reason and one more: it is a device plugged
            // into Windows for as long as the phone that asked for it is there, and no longer.
            var injector = new InputInjector();
            var pad = new VirtualPad();
            var dispatcher = new Dispatcher(injector, pad, speakers, microphone, brightness, media, display, macros, overlay);
            var session = new Session(
                args.Socket, trust, askTrust, injector, pad, dispatcher, speakers, microphone, brightness, media, display, macros, onStatus, OnSessionEnded);

            // A dedicated thread, not the thread pool: the read loop blocks for the life of the connection,
            // and input is injected from this thread directly, so nothing queues behind other work.
            var thread = new Thread(session.Run) { IsBackground = true, Name = "edgepad-session", Priority = ThreadPriority.AboveNormal };
            Session? previous;
            lock (gate)
            {
                previous = current;
                current = session;
                sessionThread = thread;
            }

            previous?.Dispose();
            thread.Start();
        }
        catch (Exception e) when (e is COMException or ObjectDisposedException or InvalidOperationException)
        {
            Log.Write($"A connection was dropped before it could be served: {e.Message}");
            args.Socket.Dispose();
        }
    }

    private void OnSessionEnded(Session session)
    {
        lock (gate)
        {
            if (current != session)
            {
                return;
            }

            current = null;
        }

        onStatus("Waiting for your phone");
    }

    public void Dispose()
    {
        try
        {
            provider?.StopAdvertising();
        }
        catch (Exception e) when (e is InvalidOperationException or COMException)
        {
            // Already stopped, or the radio went away: nothing left to stop. Throwing here left a stuck
            // process holding the single-instance lock, so no update could take over.
            Log.Write($"StopAdvertising: {e.Message}");
        }

        listener?.Dispose();
        Session? live;
        Thread? thread;
        lock (gate)
        {
            live = current;
            thread = sessionThread;
            current = null;
        }

        // Outside the lock, because the session's exit calls OnSessionEnded, which takes it. The injected keys
        // and buttons are released in Session.Run's finally on that background thread, and the process can
        // exit before it runs, leaving Alt or a mouse button held down in Windows.
        live?.Dispose();
        if (thread is { IsAlive: true })
        {
            thread.Join(ExitGrace);
        }
    }
}
