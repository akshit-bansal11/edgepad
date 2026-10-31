using System.Net.Sockets;
using Edgepad.Net;

namespace Edgepad.Network;

/// <summary>
/// A phone connected over Wi-Fi or USB tethering, once <see cref="LinkResponder"/> has let it through: the encrypted
/// stream, and the socket under it to close.
/// </summary>
internal sealed class NetworkLink(Socket socket, LinkResult result) : IPhoneLink
{
    public string Address => result.Address;

    public string Label => result.PhoneName;

    public LinkKind Kind => LinkKind.Network;

    // One stream both ways, as the socket is: SecureStream reads and writes under separate locks.
    public Stream Input => result.Secure;

    public Stream Output => result.Secure;

    private volatile bool closed;

    /// <summary>True once <see cref="Close"/> or <see cref="Release"/> has run, as every session's end does.</summary>
    public bool Closed => closed;

    public void Close()
    {
        closed = true;
        socket.Dispose();
    }

    public void Release()
    {
        // The socket first: a link refused before any session was never closed.
        Close();
        result.Secure.Dispose();
    }
}
