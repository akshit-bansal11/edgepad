using System.Runtime.InteropServices;

namespace Edgepad.Native;

/// <summary>
/// Hears the window manager change the active window: one display connection that selects PropertyChange on the root,
/// and one thread that sleeps on the connection's descriptor. It wakes once a second only to see whether it was told to
/// stop, and once per root property change; no polling of the window manager. It exists only while a phone watches.
/// </summary>
internal sealed partial class X11ActiveWatch : IDisposable
{
    private const string Xlib = "libX11.so.6";
    private const string Libc = "libc.so.6";

    // From X11/X.h: PropertyChangeMask, PropertyNotify. XPropertyEvent's atom is at 40 on LP64 (type 0, serial 8, send_event 16, display 24, window 32).
    private const nint PropertyChangeMask = 1 << 22;
    private const int PropertyNotify = 28;
    private const int AtomOffset = 40;
    private const int EventSize = 192;
    private const short PollIn = 1;
    private const int StopCheckMs = 1000;

    private readonly nint display;
    private readonly nuint activeAtom;
    private readonly Action<string> changed;
    private volatile bool stopped;

    private X11ActiveWatch(nint display, Action<string> changed)
    {
        this.display = display;
        this.changed = changed;
        activeAtom = XInternAtom(display, "_NET_ACTIVE_WINDOW", 0);
    }

    /// <summary>Null with no display. <paramref name="changed"/> gets the active window's WM_CLASS text at once, then on each change, on the watch thread.</summary>
    public static X11ActiveWatch? Start(Action<string> changed)
    {
        var display = X11Windows.Connect();
        if (display == 0)
        {
            return null;
        }

        _ = XSelectInput(display, XDefaultRootWindow(display), PropertyChangeMask);
        var watch = new X11ActiveWatch(display, changed);
        new Thread(watch.Run) { IsBackground = true, Name = "edgepad-x11-active" }.Start();
        return watch;
    }

    /// <summary>Asks the thread to stop. It closes the connection itself, within a second, so this never blocks.</summary>
    public void Dispose() => stopped = true;

    private void Run()
    {
        try
        {
            Report();
            var poll = new PollFd(XConnectionNumber(display), PollIn, 0);
            var message = new byte[EventSize];
            while (!stopped)
            {
                var activeChanged = false;
                while (XPending(display) > 0)
                {
                    _ = XNextEvent(display, message);
                    activeChanged |= BitConverter.ToInt32(message, 0) == PropertyNotify && BitConverter.ToUInt64(message, AtomOffset) == activeAtom;
                }

                if (activeChanged && !stopped)
                {
                    Report();
                }

                _ = Poll(ref poll, 1, StopCheckMs);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // Xlib went missing mid-run: the phone keeps the last app it was told.
        }
        finally
        {
            _ = XCloseDisplay(display);
        }
    }

    private void Report() => changed(X11Windows.ActiveClass(display));

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd(int fd, short events, short revents)
    {
        public int Fd = fd;
        public short Events = events;
        public short Revents = revents;
    }

    [LibraryImport(Libc, EntryPoint = "poll")]
    private static partial int Poll(ref PollFd fds, nuint count, int timeout);

    [LibraryImport(Xlib)]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport(Xlib)]
    private static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport(Xlib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint XInternAtom(nint display, string name, int onlyIfExists);

    [LibraryImport(Xlib)]
    private static partial int XSelectInput(nint display, nuint window, nint mask);

    [LibraryImport(Xlib)]
    private static partial int XConnectionNumber(nint display);

    [LibraryImport(Xlib)]
    private static partial int XPending(nint display);

    [LibraryImport(Xlib)]
    private static partial int XNextEvent(nint display, [Out] byte[] message);
}
