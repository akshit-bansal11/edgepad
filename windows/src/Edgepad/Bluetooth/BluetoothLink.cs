using System.Runtime.InteropServices;
using Edgepad.Net;
using Windows.Devices.Bluetooth;
using Windows.Networking;
using Windows.Networking.Sockets;

namespace Edgepad.Bluetooth;

/// <summary>A phone connected over RFCOMM: the WinRT socket's streams, and the socket to close.</summary>
internal sealed class BluetoothLink(StreamSocket socket) : IPhoneLink
{
    public string Address { get; } = socket.Information.RemoteHostName.RawName;

    // Windows holds the name of a bonded device. Asked for once, at once; until it answers (or if it never does) the
    // phone is called by its address, as before.
    private readonly Task<string?> name = LookUpName(socket.Information.RemoteHostName);

    public string Label => name.IsCompletedSuccessfully && name.Result is { Length: > 0 } known ? known : Address;

    public LinkKind Kind => LinkKind.Bluetooth;

    // The WinRT adapters read with partial-read semantics, so the default buffer returns as soon as
    // any bytes arrive — it saves per-byte calls without holding data back.
    public Stream Input { get; } = socket.InputStream.AsStreamForRead();

    public Stream Output { get; } = socket.OutputStream.AsStreamForWrite();

    private static async Task<string?> LookUpName(HostName host)
    {
        try
        {
            using var device = await BluetoothDevice.FromHostNameAsync(host);
            return device?.Name;
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            // A name is a nicety: the address names the phone just as well for the log and the trust question.
            Log.Write($"The Bluetooth device name could not be read: {e.Message}");
            return null;
        }
    }

    public void Close() => socket.Dispose();

    public void Release()
    {
        // The socket again first: a link refused before any session was never closed.
        socket.Dispose();
        Input.Dispose();
        try
        {
            Output.Dispose();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or COMException or OperationCanceledException)
        {
            // Its buffer may still hold the aborted write, which disposing tries to flush to a closed
            // socket. Those bytes were never going to arrive.
        }
    }
}
