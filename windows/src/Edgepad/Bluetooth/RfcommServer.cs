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
    AudioEndpoint speakers,
    AudioEndpoint microphone,
    BrightnessControl brightness,
    MediaSessions media,
    DisplayModes display,
    MacroStore macros,
    LevelOverlay overlay) : IDisposable
{
    private readonly Lock gate = new();
    private RfcommServiceProvider? provider;
    private StreamSocketListener? listener;
    private Session? current;

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

    private void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        // A device this laptop already knows it will refuse must never get far enough to replace the phone
        // that is connected. The check that grants trust stays on the session thread, after HELLO, because
        // trust on first use must not be handed to something that has not proved it speaks this protocol —
        // but that check runs *after* the new session has displaced the old one, so without this any other
        // bonded device could drop the owner's link at will just by connecting, over and over.
        //
        // A different trusted address refuses here, and so does a trust file that cannot be read, since the
        // session would refuse it anyway. Only nothing trusted yet goes on to the session to earn it.
        var address = args.Socket.Information.RemoteHostName.RawName;
        if (trust.Refuses(address))
        {
            Log.Write($"Refused {address} without disturbing the connected phone: this laptop trusts {trust.Trusted}");
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
            args.Socket, trust, injector, pad, dispatcher, speakers, microphone, brightness, media, display, macros, onStatus, OnSessionEnded);
        Session? previous;
        lock (gate)
        {
            previous = current;
            current = session;
        }

        previous?.Dispose();

        // A dedicated thread, not the thread pool: the read loop blocks for the life of the connection,
        // and input is injected from this thread directly, so nothing queues behind other work.
        new Thread(session.Run) { IsBackground = true, Name = "edgepad-session", Priority = ThreadPriority.AboveNormal }
            .Start();
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
        lock (gate)
        {
            current?.Dispose();
            current = null;
        }
    }
}
