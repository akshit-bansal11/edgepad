using System.Runtime.InteropServices;
using Edgepad.Net;
using Windows.Networking.Sockets;

namespace Edgepad.Bluetooth;

/// <summary>A phone connected over RFCOMM: the WinRT socket's streams, and the socket to close.</summary>
internal sealed class BluetoothLink(StreamSocket socket) : IPhoneLink
{
    public string Address { get; } = socket.Information.RemoteHostName.RawName;

    public string Label => Address;

    public LinkKind Kind => LinkKind.Bluetooth;

    // The WinRT adapters read with partial-read semantics, so the default buffer returns as soon as
    // any bytes arrive — it saves per-byte calls without holding data back.
    public Stream Input { get; } = socket.InputStream.AsStreamForRead();

    public Stream Output { get; } = socket.OutputStream.AsStreamForWrite();

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
