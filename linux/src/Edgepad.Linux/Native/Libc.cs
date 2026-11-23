using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Edgepad.Native;

/// <summary>
/// The two socket calls .NET has no managed way to make on a descriptor it did not open itself. glibc by its
/// versioned name: the plain <c>libc.so</c> is a linker script, present only with the development headers.
/// </summary>
internal static partial class Libc
{
    private const string Library = "libc.so.6";

    // Linux values, from fcntl.h and sys/socket.h. These are the asm-generic values that x86_64 and arm64 share, but
    // they have only been checked against x86_64: UNVERIFIED on an arm64 device.
    private const int GetFlags = 3;
    private const int SetFlags = 4;
    private const int NonBlocking = 0x800;
    private const int ShutReadWrite = 2;

    /// <summary>
    /// Clears O_NONBLOCK. The session reads on a thread of its own with plain blocking reads, as on Windows; a
    /// non-blocking descriptor would answer the first read with EAGAIN, which a FileStream reports as a failure.
    /// BlueZ does not document which mode it hands the socket over in, so this does not assume either.
    /// </summary>
    public static void MakeBlocking(SafeHandle socket) => With(socket, fd =>
    {
        // ponytail: fcntl is variadic and is called here as if it took three ints. That is the same call on the
        // x86_64 SysV ABI for integer arguments. The arm64 Linux calling convention is believed to treat a variadic
        // call the same way for integer and pointer arguments, so the same declaration should hold there, but that is
        // UNVERIFIED on an arm64 device; if it fails there, a C shim or .NET's own System.Native is the way out.
        var flags = Fcntl(fd, GetFlags, 0);
        if (flags < 0 || ((flags & NonBlocking) != 0 && Fcntl(fd, SetFlags, flags & ~NonBlocking) < 0))
        {
            throw new IOException("The RFCOMM socket could not be made blocking", new Win32Exception(Marshal.GetLastPInvokeError()));
        }

        return 0;
    });

    /// <summary>
    /// Ends both directions of the socket. This, not closing it, is what wakes a read blocked on another thread:
    /// close(2) on Linux leaves a read already in the kernel waiting, and the SafeHandle would not close the
    /// descriptor under that read anyway. The read then sees end of stream; a write blocked in the kernel fails
    /// (.NET ignores SIGPIPE, so that is an error code, not a signal that ends the process).
    /// </summary>
    public static void Shutdown(SafeHandle socket) =>
        // A socket the phone already dropped answers ENOTCONN, which leaves nothing to wake: not an error here.
        With(socket, fd => ShutdownSocket(fd, ShutReadWrite));

    /// <summary>Asks process <paramref name="pid"/> to end (SIGTERM, 15, as a logout does). False when it could not be signalled.</summary>
    public static bool Terminate(int pid) => Kill(pid, 15) == 0;

    /// <summary>Runs <paramref name="call"/> on the descriptor, holding the handle so it cannot be closed and reused meanwhile.</summary>
    private static int With(SafeHandle handle, Func<int, int> call)
    {
        var added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            return call((int)handle.DangerousGetHandle());
        }
        finally
        {
            if (added)
            {
                handle.DangerousRelease();
            }
        }
    }

    [LibraryImport(Library, EntryPoint = "fcntl", SetLastError = true)]
    private static partial int Fcntl(int fd, int command, int argument);

    [LibraryImport(Library, EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int pid, int signal);

    [LibraryImport(Library, EntryPoint = "shutdown", SetLastError = true)]
    private static partial int ShutdownSocket(int fd, int how);
}
