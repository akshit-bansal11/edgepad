using System.Runtime.InteropServices;

namespace Edgepad.Native;

/// <summary>
/// Makes an X11 window click-through: an empty input shape, so the server hands every click on it to whatever is
/// underneath. Avalonia 12.1.4 does this for its popups (X11Window.SetHitTestVisible) but offers no way to ask for
/// it on a Window, so it is done here, on a display connection of this app's own: an X window id means the same
/// window on every connection to one server. libX11 and libXext by their versioned names, as libc is.
/// </summary>
internal static partial class X11
{
    private const string Xlib = "libX11.so.6";
    private const string Xext = "libXext.so.6";

    // From xorgproto's X11/extensions/shapeconst.h (ShapeSet, ShapeInput) and X11/X.h (Unsorted).
    private const int ShapeSet = 0;
    private const int ShapeInput = 2;
    private const int Unsorted = 0;

    /// <summary>
    /// Sets an empty input region on <paramref name="window"/> and every window inside it. The children matter: with
    /// a GPU backend Avalonia draws into a child window, and an input shape on the parent alone leaves that child
    /// catching the pointer (Avalonia's own SetHitTestVisible shapes both for this reason). False when no display can
    /// be opened or the server has no SHAPE extension (version 1.1 is the first with input shapes).
    /// <para>
    /// An X error (a window that has gone) does not end the process: Avalonia installs its own process-wide error
    /// handler, which records the error and returns, before any window exists to call this on.
    /// </para>
    /// </summary>
    public static bool ClickThrough(nuint window)
    {
        // Null: the display named by $DISPLAY, which is the one Avalonia opened.
        var display = XOpenDisplay(0);
        if (display == 0)
        {
            return false;
        }

        try
        {
            if (XShapeQueryExtension(display, out _, out _) == 0
                || XShapeQueryVersion(display, out var major, out var minor) == 0
                || major < 1 || (major == 1 && minor < 1))
            {
                return false;
            }

            Shape(display, window);
            return true;
        }
        finally
        {
            // XCloseDisplay sends what is queued and ends with an XSync, so the shape is applied before this returns
            // and no separate XFlush is needed.
            _ = XCloseDisplay(display);
        }
    }

    private static void Shape(nint display, nuint window)
    {
        // No rectangles with ShapeSet: the input region becomes empty.
        XShapeCombineRectangles(display, window, ShapeInput, 0, 0, 0, 0, ShapeSet, Unsorted);
        if (XQueryTree(display, window, out _, out _, out var children, out var count) == 0)
        {
            return;
        }

        try
        {
            for (var i = 0; i < count; i++)
            {
                // Window is an unsigned long, pointer-sized on linux-x64.
                Shape(display, (nuint)Marshal.ReadIntPtr(children, i * IntPtr.Size));
            }
        }
        finally
        {
            if (children != 0)
            {
                _ = XFree(children);
            }
        }
    }

    [LibraryImport(Xlib)]
    private static partial nint XOpenDisplay(nint name);

    [LibraryImport(Xlib)]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport(Xlib)]
    private static partial int XQueryTree(
        nint display, nuint window, out nuint root, out nuint parent, out nint children, out uint count);

    [LibraryImport(Xlib)]
    private static partial int XFree(nint data);

    [LibraryImport(Xext)]
    private static partial int XShapeQueryExtension(nint display, out int eventBase, out int errorBase);

    [LibraryImport(Xext)]
    private static partial int XShapeQueryVersion(nint display, out int major, out int minor);

    [LibraryImport(Xext)]
    private static partial void XShapeCombineRectangles(
        nint display, nuint window, int kind, int xOffset, int yOffset, nint rectangles, int count, int operation, int ordering);
}
