using System.Net.Sockets;
using System.Runtime.Versioning;
using Edgepad.Hooks;

namespace Edgepad.Dashboard;

/// <summary>
/// Where the Linux app listens for the plugin's hook client: core's <see cref="UnixSocketListener"/> (folder 0700, socket 0600,
/// SO_PEERCRED against this process's euid before a byte is read) in <c>$XDG_RUNTIME_DIR/edgepad</c>, the folder hook.json lives in.
/// There is no fallback: without XDG_RUNTIME_DIR, or when the folder fails its checks, nothing listens and the Claude and Codex
/// integrations report <c>unsupported</c> rather than use a folder other users can reach.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class LinuxHookListener
{
    /// <summary>The listener in <paramref name="folder"/> (<see cref="HookEndpoint.SocketDirectory"/> in the app); null, logged, when there is none.</summary>
    /// <param name="peerUid">For tests: the uid of a connected peer. SO_PEERCRED by default.</param>
    /// <param name="myUid">For tests: this process's uid.</param>
    public static UnixSocketListener? Open(string? folder, Func<Socket, int>? peerUid = null, int? myUid = null)
    {
        if (folder is null)
        {
            Log.Write("XDG_RUNTIME_DIR is not set, so the Claude Code and Codex integrations are not offered");
            return null;
        }

        try
        {
            return new UnixSocketListener(folder, peerUid, myUid);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SocketException)
        {
            // The type and message name the folder at most, never anything a client sent.
            Log.Write($"The hook socket could not be opened, so the Claude Code and Codex integrations are not offered: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }
}
