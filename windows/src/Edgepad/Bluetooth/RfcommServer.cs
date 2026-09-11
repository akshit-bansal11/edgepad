using Edgepad.Protocol;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace Edgepad.Bluetooth;

/// <summary>
/// Advertises the Edgepad RFCOMM service and serves one phone at a time. A new connection replaces the
/// old one rather than being refused: after a dropped link the phone reconnects before the old socket
/// has noticed it is dead, and refusing would lock the phone out until it did.
/// </summary>
internal sealed class RfcommServer(Action<string> onStatus) : IDisposable
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
        var session = new Session(args.Socket, onStatus, OnSessionEnded);
        Session? previous;
        lock (gate)
        {
            previous = current;
            current = session;
        }

        previous?.Dispose();

        // A dedicated thread, not the thread pool: the read loop blocks for the life of the connection,
        // and input frames are injected from this thread directly, so nothing queues behind other work.
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
        provider?.StopAdvertising();
        listener?.Dispose();
        lock (gate)
        {
            current?.Dispose();
            current = null;
        }
    }
}
