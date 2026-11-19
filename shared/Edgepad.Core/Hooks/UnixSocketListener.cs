using System.Globalization;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Edgepad.Hooks;

/// <summary>
/// The hook channel on Linux: <c>hook.sock</c> in a folder only this user may enter. The folder is made 0700 and refused when it is
/// a link or has any other mode; a 0700 folder this process can write in is its owner's (or root's), which is the owner check. A
/// stale socket is removed, the new one is bound, then made 0600. Every connection's peer uid (SO_PEERCRED) must equal this
/// process's effective uid, checked before a byte is read; any other peer is closed at once.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class UnixSocketListener : IHookListener
{
    public const string SocketName = "hook.sock";

    // <sys/socket.h> on every Linux architecture .NET runs on.
    private const int SolSocket = 1;
    private const int SoPeerCred = 17;

    private const UnixFileMode PrivateFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly Socket socket;
    private readonly Func<Socket, int> peerUid;
    private readonly int myUid;
    private volatile bool disposed;

    /// <summary>Binds <c>hook.sock</c> in <paramref name="directory"/>. Throws <see cref="IOException"/> when the folder or socket fails its checks.</summary>
    /// <param name="peerUid">For tests: the uid of a connected peer. SO_PEERCRED by default.</param>
    /// <param name="myUid">For tests: this process's uid. Read from /proc/self/status by default.</param>
    public UnixSocketListener(string directory, Func<Socket, int>? peerUid = null, int? myUid = null)
    {
        this.peerUid = peerUid ?? PeerUid;
        this.myUid = myUid ?? EffectiveUid();
        CheckFolder(directory);
        Endpoint = Path.Combine(directory, SocketName);
        File.Delete(Endpoint);

        socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(Endpoint));
            File.SetUnixFileMode(Endpoint, PrivateFile);
            CheckFolder(directory);
            socket.Listen(8);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public string Endpoint { get; }

    public async Task<Stream?> AcceptAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Socket client;
            try
            {
                client = await socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
            catch (SocketException) when (disposed)
            {
                return null;
            }

            int uid;
            try
            {
                uid = peerUid(client);
            }
            catch (SocketException)
            {
                uid = -1;
            }

            if (uid == myUid && uid >= 0)
            {
                return new NetworkStream(client, ownsSocket: true);
            }

            Log.Write("A hook connection from another user was closed");
            client.Dispose();
        }
    }

    public void Dispose()
    {
        disposed = true;
        socket.Dispose();
        try
        {
            File.Delete(Endpoint);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Removed with the folder's next bind.
        }
    }

    // Creates the folder 0700 when missing; refuses a link or a folder with any other mode.
    private static void CheckFolder(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory, PrivateFolder);
        }

        var info = new DirectoryInfo(directory);
        if (info.LinkTarget is not null)
        {
            throw new IOException("The hook folder is a link");
        }

        if (File.GetUnixFileMode(directory) != PrivateFolder)
        {
            throw new IOException("The hook folder is not private to this user");
        }
    }

    // struct ucred { pid_t pid; uid_t uid; gid_t gid; }
    private static int PeerUid(Socket client)
    {
        Span<byte> cred = stackalloc byte[12];
        return client.GetRawSocketOption(SolSocket, SoPeerCred, cred) == 12 ? BitConverter.ToInt32(cred[4..8]) : -1;
    }

    // The second field of the "Uid:" line is the effective uid.
    private static int EffectiveUid()
    {
        foreach (var line in File.ReadLines("/proc/self/status"))
        {
            if (line.StartsWith("Uid:", StringComparison.Ordinal))
            {
                var fields = line[4..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length >= 2 && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var uid))
                {
                    return uid;
                }
            }
        }

        throw new IOException("The effective uid could not be read");
    }
}
