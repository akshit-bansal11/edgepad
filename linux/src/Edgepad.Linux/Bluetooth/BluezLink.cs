using Edgepad.Native;
using Edgepad.Net;
using Microsoft.Win32.SafeHandles;

namespace Edgepad.Bluetooth;

/// <summary>
/// A phone connected over the RFCOMM socket BlueZ handed over. Takes ownership of the socket: its two streams
/// close the descriptor when the session disposes them.
/// </summary>
internal sealed class BluezLink : IPhoneLink
{
    private readonly SafeFileHandle socket;

    public BluezLink(SafeFileHandle socket, string address)
    {
        this.socket = socket;
        Address = address;

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
        Input = new FileStream(socket, FileAccess.Read, bufferSize: 4096);
        Output = new FileStream(socket, FileAccess.Write, bufferSize: 0);
    }

    /// <summary>The phone's Bluetooth address, as the log and the trust file name it.</summary>
    public string Address { get; }

    public string Label => Address;

    public LinkKind Kind => LinkKind.Bluetooth;

    public Stream Input { get; }

    public Stream Output { get; }

    /// <summary>
    /// Shuts the socket down: this, not closing it, fails a write blocked on it and ends a blocked read. The
    /// descriptor stays open until the streams are disposed, never under a writer's feet.
    /// </summary>
    public void Close()
    {
        try
        {
            Libc.Shutdown(socket);
        }
        catch (ObjectDisposedException)
        {
            // Already closed: nothing left to wake.
        }
    }
}
