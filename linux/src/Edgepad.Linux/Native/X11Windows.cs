using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Edgepad.Native;

/// <summary>One top-level app window, as the window manager lists it.</summary>
internal sealed record ClientWindow(nuint Id, string? Image, IReadOnlyList<string> Classes, bool Hidden);

/// <summary>The app windows in the order they were first mapped, and the active one, 0 when there is none.</summary>
internal sealed record X11Desktop(IReadOnlyList<ClientWindow> Windows, nuint Active);

/// <summary>
/// Other apps' windows, through the window manager's EWMH hints, on a display connection opened for each call as
/// <see cref="X11"/> does. Every act is a client message to the root window, which the window manager carries out: it
/// is a post, so a hung app cannot stall the caller. Source indication 2 says a pager asked, which window managers
/// honour more readily than an application.
/// </summary>
internal static partial class X11Windows
{
    private const string Xlib = "libX11.so.6";

    // From X11/X.h: ClientMessage, SubstructureNotifyMask | SubstructureRedirectMask.
    private const int ClientMessageType = 33;
    private const nint RootMask = 0x180000;

    // XEvent is a union padded with long[24]. The offsets are XClientMessageEvent's on LP64, read from X11/Xlib.h: int
    // type (0), unsigned long serial (8), Bool send_event (16), Display* display (24), Window window (32), Atom
    // message_type (40), int format (48), then data, which starts on a long boundary (56) and is long[5] for format 32.
    private const int EventSize = 192;
    private const int WindowOffset = 32;
    private const int MessageTypeOffset = 40;
    private const int FormatOffset = 48;
    private const int DataOffset = 56;

    private const long PagerSource = 2;
    private const long IconicState = 3; // ICCCM's WM_CHANGE_STATE

    // XGetWindowProperty counts its length in 32-bit units whatever the format.
    private const int MaxUnits = 1024;

    private static readonly Lazy<bool> Prepared = new(Prepare);

    /// <summary>Whether a display can be opened at all.</summary>
    public static bool Opens()
    {
        var display = Open();
        if (display == 0)
        {
            return false;
        }

        _ = XCloseDisplay(display);
        return true;
    }

    /// <summary>
    /// The desktop as the window manager has it, or null with no display. A window that goes during the pass reads as
    /// empty and matches nothing. <paramref name="imageOf"/> is asked once per process.
    /// </summary>
    public static X11Desktop? Read(Func<uint, string?> imageOf)
    {
        var display = Open();
        if (display == 0)
        {
            return null;
        }

        try
        {
            var root = XDefaultRootWindow(display);
            var pidAtom = XInternAtom(display, "_NET_WM_PID", 0);
            var stateAtom = XInternAtom(display, "_NET_WM_STATE", 0);
            var hiddenAtom = XInternAtom(display, "_NET_WM_STATE_HIDDEN", 0);
            var classAtom = XInternAtom(display, "WM_CLASS", 0);
            var images = new Dictionary<uint, string?>();
            var windows = new List<ClientWindow>();
            foreach (var window in Longs(display, root, XInternAtom(display, "_NET_CLIENT_LIST", 0)))
            {
                var pid = Longs(display, window, pidAtom) is [var id, ..] ? (uint)id : 0;
                if (!images.TryGetValue(pid, out var image))
                {
                    image = imageOf(pid);
                    images[pid] = image;
                }

                // WM_CLASS is two strings, instance then class, each ended by a NUL.
                var classes = Encoding.Latin1.GetString(Property(display, window, classAtom, 8))
                    .Split('\0', StringSplitOptions.RemoveEmptyEntries);
                windows.Add(new ClientWindow(window, image, classes, Longs(display, window, stateAtom).Contains(hiddenAtom)));
            }

            var active = Longs(display, root, XInternAtom(display, "_NET_ACTIVE_WINDOW", 0)) is [var current, ..] ? current : 0;
            return new X11Desktop(windows, active);
        }
        finally
        {
            _ = XCloseDisplay(display);
        }
    }

    /// <summary>Activates the window, which also brings a minimized one back. False when nothing could be sent.</summary>
    public static bool Raise(nuint window) => Send("_NET_ACTIVE_WINDOW", [window], RaiseMessage);

    /// <summary>Asks for the window to be iconified. False when nothing could be sent.</summary>
    public static bool Minimize(nuint window) => Send("WM_CHANGE_STATE", [window], MinimizeMessage);

    /// <summary>
    /// Asks the window manager to close each window, which it does with WM_DELETE_WINDOW, so the app may ask to save.
    /// False when nothing could be sent.
    /// </summary>
    public static bool Close(IReadOnlyList<nuint> windows) => Send("_NET_CLOSE_WINDOW", windows, CloseMessage);

    /// <summary>_NET_ACTIVE_WINDOW: source, then timestamp and the requestor's active window, both 0.</summary>
    internal static byte[] RaiseMessage(nuint window, nuint messageType) => ClientMessage(window, messageType, PagerSource, 0);

    /// <summary>WM_CHANGE_STATE: the state wanted.</summary>
    internal static byte[] MinimizeMessage(nuint window, nuint messageType) => ClientMessage(window, messageType, IconicState, 0);

    /// <summary>_NET_CLOSE_WINDOW: timestamp first, 0, and only then the source.</summary>
    internal static byte[] CloseMessage(nuint window, nuint messageType) => ClientMessage(window, messageType, 0, PagerSource);

    /// <summary>A format 32 XClientMessageEvent as Xlib lays it out. The fields left 0 are ones XSendEvent fills in or ignores.</summary>
    private static byte[] ClientMessage(nuint window, nuint messageType, long first, long second)
    {
        var message = new byte[EventSize];
        BinaryPrimitives.WriteInt32LittleEndian(message, ClientMessageType);
        BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(WindowOffset), window);
        BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(MessageTypeOffset), messageType);
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(FormatOffset), 32);
        BinaryPrimitives.WriteInt64LittleEndian(message.AsSpan(DataOffset), first);
        BinaryPrimitives.WriteInt64LittleEndian(message.AsSpan(DataOffset + sizeof(long)), second);
        return message;
    }

    private static bool Send(string messageName, IReadOnlyList<nuint> windows, Func<nuint, nuint, byte[]> message)
    {
        var display = Open();
        if (display == 0)
        {
            return false;
        }

        try
        {
            var root = XDefaultRootWindow(display);
            var messageType = XInternAtom(display, messageName, 0);
            var sent = false;
            foreach (var window in windows)
            {
                sent |= XSendEvent(display, root, 0, RootMask, message(window, messageType)) != 0;
            }

            return sent;
        }
        finally
        {
            // XCloseDisplay sends what is queued, as in X11.ClickThrough, so no separate XFlush is needed.
            _ = XCloseDisplay(display);
        }
    }

    private static nint Open()
    {
        _ = Prepared.Value;

        // Null: the display named by $DISPLAY.
        return XOpenDisplay(0);
    }

    /// <summary>
    /// Once, before this class's first Xlib call. A window can go between being listed and being read, and Xlib's
    /// default answer to that BadWindow is to end the process. Avalonia's handler (see <see cref="X11"/>) cannot be
    /// relied on here: the server takes phones before the tray starts, and without a display the tray never does.
    /// Xlib keeps one handler per process, so this may replace Avalonia's, or be replaced by it; either one returns.
    /// Connections on several threads also need XInitThreads, which is safe to call again.
    /// </summary>
    private static unsafe bool Prepare()
    {
        _ = XInitThreads();
        _ = XSetErrorHandler(&IgnoreError);
        return true;
    }

    [UnmanagedCallersOnly]
    private static int IgnoreError(nint display, nint error) => 0;

    /// <summary>A format 32 property. Xlib hands those over as an array of long, so eight bytes each here.</summary>
    private static ReadOnlySpan<nuint> Longs(nint display, nuint window, nuint property) =>
        MemoryMarshal.Cast<byte, nuint>(Property(display, window, property, 32));

    /// <summary>The property's bytes, or none when it is missing, of another format, or the window has gone.</summary>
    private static byte[] Property(nint display, nuint window, nuint property, int format)
    {
        // Type 0 is AnyPropertyType.
        if (XGetWindowProperty(display, window, property, 0, MaxUnits, 0, 0, out _, out var actual, out var items, out _, out var data) != 0)
        {
            return [];
        }

        try
        {
            if (data == 0 || actual != format)
            {
                return [];
            }

            var bytes = new byte[(int)items * (format == 32 ? IntPtr.Size : 1)];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            if (data != 0)
            {
                _ = XFree(data);
            }
        }
    }

    [LibraryImport(Xlib)]
    private static partial int XInitThreads();

    [LibraryImport(Xlib)]
    private static unsafe partial nint XSetErrorHandler(delegate* unmanaged<nint, nint, int> handler);

    [LibraryImport(Xlib)]
    private static partial nint XOpenDisplay(nint name);

    [LibraryImport(Xlib)]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport(Xlib)]
    private static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport(Xlib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint XInternAtom(nint display, string name, int onlyIfExists);

    [LibraryImport(Xlib)]
    private static partial int XGetWindowProperty(
        nint display,
        nuint window,
        nuint property,
        nint offset,
        nint length,
        int delete,
        nuint type,
        out nuint actualType,
        out int actualFormat,
        out nuint items,
        out nuint bytesAfter,
        out nint data);

    [LibraryImport(Xlib)]
    private static partial int XSendEvent(nint display, nuint window, int propagate, nint mask, byte[] message);

    [LibraryImport(Xlib)]
    private static partial int XFree(nint data);
}
