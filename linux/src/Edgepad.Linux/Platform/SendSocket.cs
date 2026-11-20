using System.Net.Sockets;
using System.Text;

namespace Edgepad.Platform;

/// <summary>
/// How <c>edgepad --send FILE</c> (the "Send to phone" desktop entry) reaches the running copy: a Unix socket in
/// <c>$XDG_RUNTIME_DIR</c>, a folder only its owner can enter, with the socket itself mode 0600. A connection carries absolute file
/// paths, one per line; each that names a regular file is handed to <c>onPath</c>. The file goes to the connected phone and
/// to nobody else; nothing here reads it.
/// </summary>
internal sealed class SendSocket : IDisposable
{
    private const int MaxBytes = 64 * 1024;
    private const int MaxPaths = 16;
    private static readonly TimeSpan ReadLimit = TimeSpan.FromSeconds(5);

    private readonly Socket listener;
    private readonly string path;
    private readonly Action<string> onPath;
    private volatile bool stopped;

    private SendSocket(Socket listener, string path, Action<string> onPath)
    {
        this.listener = listener;
        this.path = path;
        this.onPath = onPath;
    }

    /// <summary>Where the running copy listens, or null with no <c>$XDG_RUNTIME_DIR</c> (then there is no send socket at all).</summary>
    public static string? SocketPath
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            return !string.IsNullOrEmpty(dir) && Path.IsPathRooted(dir) ? Path.Combine(dir, "edgepad-send.sock") : null;
        }
    }

    /// <summary>Listens and serves on a thread of its own. Null, logged, when it cannot; sending from the tray still works.</summary>
    public static SendSocket? Listen(Action<string> onPath)
    {
        if (SocketPath is not { } path)
        {
            return null;
        }

        Socket? socket = null;
        try
        {
            // Only one copy runs (the single-instance lock is held), so a socket file already here is a dead one's.
            File.Delete(path);
            socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(path));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            socket.Listen(4);
        }
        catch (Exception e) when (e is SocketException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"The send socket {path} could not be opened: {e.Message}");
            socket?.Dispose();
            return null;
        }

        var served = new SendSocket(socket, path, onPath);
        new Thread(served.Serve) { IsBackground = true, Name = "edgepad-send" }.Start();
        return served;
    }

    /// <summary>The absolute paths in <paramref name="text"/>, one per line, at most 16; relative paths and blank lines are dropped.</summary>
    internal static IReadOnlyList<string> ParsePaths(string text) =>
        [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Path.IsPathRooted).Take(MaxPaths)];

    /// <summary>Hands <paramref name="files"/> to the running copy. False when none is listening.</summary>
    public static bool Send(IEnumerable<string> files)
    {
        if (SocketPath is not { } path)
        {
            return false;
        }

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(path));
            socket.Send(Encoding.UTF8.GetBytes(string.Join('\n', files.Select(Path.GetFullPath)) + "\n"));
            return true;
        }
        catch (Exception e) when (e is SocketException or IOException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        stopped = true;
        listener.Dispose();
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The send socket {path} could not be removed: {e.Message}");
        }
    }

    private void Serve()
    {
        while (true)
        {
            try
            {
                using var client = listener.Accept();
                client.ReceiveTimeout = (int)ReadLimit.TotalMilliseconds;
                var buffer = new byte[MaxBytes];
                var length = 0;
                int read;
                while (length < MaxBytes && (read = client.Receive(buffer, length, MaxBytes - length, SocketFlags.None)) > 0)
                {
                    length += read;
                }

                foreach (var file in ParsePaths(Encoding.UTF8.GetString(buffer, 0, length)).Where(File.Exists))
                {
                    onPath(file);
                }
            }
            catch (Exception) when (stopped)
            {
                return;
            }
            catch (Exception e) when (e is SocketException or IOException or ObjectDisposedException)
            {
                // One bad connection (a timeout, a hang-up) costs only itself.
            }
        }
    }
}
