using System.Globalization;
using Avalonia;
using Avalonia.Threading;
using Edgepad.Annotation;
using Edgepad.Protocol;

namespace Edgepad.Ui;

/// <summary>
/// One connection's annotate and sign sessions: the strokes, the overlay that shows them, and the signature export,
/// as the Windows and Mac apps have them. Frames arrive on the session's reader and change the board there, under a
/// lock, so each one can be counted as taken or dropped at once; the window lives on the UI thread and only ever
/// reads a copy.
/// <para>
/// There is one overlay for the app, built hidden when the tray starts (<see cref="StartUi"/>), because the screen
/// size the phone needs is read through a window and the session may start before any stroke. Whether annotate is
/// offered (TEXT 9) is decided then, before a phone could ask: the window must be an X11 one (always so with Avalonia
/// 12.1.4 on Linux, which only has an X11 backend, so Wayland means XWayland), the X server must take an empty input
/// shape, and a compositor must make it see-through. If showing it fails later, annotate is turned off and the phone
/// told again. Best effort: whether a Wayland compositor keeps an XWayland window above its own, and passes clicks
/// through it, is UNKNOWN (docs/LINUX.md).
/// </para>
/// </summary>
internal sealed class Annotator : IDisposable
{
    private readonly Lock gate = new();
    private readonly StrokeBoard board = new();

    // Set once, on the session's reader, before the first frame is read.
    private Action<Frame>? send;
    private int disposed;
    private int posted;

    /// <summary>The primary screen in physical pixels, read on the UI thread; null until the UI is up.</summary>
    private static volatile PrimaryScreen? primary;

    /// <summary>Set once the UI thread is running, so posts to it are ever served.</summary>
    private static volatile bool uiUp;

    /// <summary>Whether the overlay can be shown: set by <see cref="StartUi"/>, cleared when showing it fails.</summary>
    private static volatile bool annotating;

    /// <summary>Only touched on the UI thread.</summary>
    private static AnnotationOverlay? overlay;

    /// <summary>The session whose strokes the overlay shows, so a replaced session cannot hide the new one's. UI thread only.</summary>
    private static Annotator? owner;

    /// <summary>Raised on the UI thread when the screen or what is offered changes.</summary>
    private static event Action? Changed;

    /// <summary>The TEXT 9 tokens for a laptop that can, or cannot, show the overlay. Capture is always offered.</summary>
    internal static string Features(bool annotate) => annotate ? $"{Feature.Annotate}/{Feature.Capture}" : Feature.Capture;

    /// <summary>
    /// Why the overlay cannot be used, or null when it can: the window is not an X11 one, there is no compositor
    /// (the window would cover the screen opaque), or the input shape could not be set (it would eat every click).
    /// <paramref name="clickThrough"/> is only tried on an X11 window.
    /// </summary>
    internal static string? Refusal(string? handleDescriptor, bool transparent, Func<bool> clickThrough) =>
        handleDescriptor != "XID" ? $"the window is not an X11 one ({handleDescriptor ?? "no handle"})"
        : !transparent ? "no compositor is running, so the window could not be see-through"
        : !clickThrough() ? "the X server did not take an empty input shape (no SHAPE 1.1, or no display)"
        : null;

    /// <summary>
    /// Builds the overlay, hidden, reads the primary screen and decides whether annotate is offered. Once, on the UI
    /// thread, when the tray starts; sessions that began before it are told the outcome.
    /// </summary>
    public static void StartUi()
    {
        uiUp = true;
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "unknown";
        try
        {
            var window = new AnnotationOverlay();
            overlay = window;
            ReadPrimary(window);
            if (window.Screens is { } screens)
            {
                screens.Changed += (_, _) =>
                {
                    ReadPrimary(window);
                    Changed?.Invoke();
                };
            }

            var refusal = window.Prepare();
            annotating = refusal is null;
            Log.Write(refusal is null
                ? $"Annotate is offered ({session} session, through X11)"
                : $"Annotate is not offered ({session} session): {refusal}");
        }
        catch (Exception e)
        {
            Log.Write($"The annotation overlay could not be built, so annotate is not offered ({session} session): {e}");
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Sends TEXT 9 and TEXT 10 through <paramref name="sink"/>, now and whenever they change, and keeps it for the
    /// TEXT 11 answers. On the session's reader, after the handshake: a dead socket here ends the session.
    /// </summary>
    public void Start(Action<Frame> sink)
    {
        send = sink;
        Changed += OnChanged;
        Report();
    }

    /// <summary>One PEN frame. False means dropped, by the board's rules.</summary>
    public bool Handle(Pen pen)
    {
        bool taken;
        bool shown;
        lock (gate)
        {
            taken = board.Pen(pen.X, pen.Y, pen.State);
            shown = board.Mode == BoardMode.Annotate;
        }

        // Signing draws nothing here, so its points cost no repaint.
        if (taken && shown)
        {
            Redraw();
        }

        return taken;
    }

    /// <summary>The colour of the next stroke. False when the index is not in the palette.</summary>
    public bool SetColour(byte index)
    {
        lock (gate)
        {
            return board.SetColour(index);
        }
    }

    /// <summary>One of the annotate and sign actions, 40..51. False means dropped.</summary>
    public bool Run(ActionId id)
    {
        if (id == ActionId.SignSave)
        {
            return SaveSignature();
        }

        // A phone that has not yet heard annotate was turned off: nothing could show its strokes.
        if (id == ActionId.AnnotateBegin && !annotating)
        {
            return false;
        }

        bool taken;
        lock (gate)
        {
            taken = Apply(id);
        }

        if (taken)
        {
            Redraw();
        }

        return taken;
    }

    /// <summary>
    /// Ends whatever is open and takes the overlay down. Called from the session's own cleanup, so a phone that
    /// vanishes mid-drawing leaves no layer over the owner's screen.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Changed -= OnChanged;
        lock (gate)
        {
            board.End();
        }

        // A post, so the window is only ever touched on the UI thread; a drain still queued runs first and sees the
        // session gone.
        Post(Remove);
    }

    private static void ReadPrimary(AnnotationOverlay window)
    {
        // Some X setups mark no output primary; the first one is then the one the phone's span maps to.
        var screens = window.Screens;
        var screen = screens?.Primary ?? (screens?.All is { Count: > 0 } all ? all[0] : null);
        primary = screen is null ? null : new PrimaryScreen(screen.Bounds);
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the UI thread, when there is one. Nothing may escape a post: it would end the
    /// app from inside its loop.
    /// </summary>
    private static void Post(Action work)
    {
        if (!uiUp)
        {
            return;
        }

        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    work();
                }
                catch (Exception e)
                {
                    Log.Write($"Annotation work on the UI thread failed: {e}");
                }
            });
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            // The UI loop has gone, so Edgepad is quitting, and the window goes with the process.
        }
    }

    private void OnChanged() => _ = Task.Run(() =>
    {
        try
        {
            Report();
        }
        catch (Exception e)
        {
            // A dead link is the reader's to notice and end; a lost report costs nothing more.
            Log.Write($"The screen size or features could not be sent: {e.Message}");
        }
    });

    private void Report()
    {
        if (Volatile.Read(ref disposed) != 0 || send is not { } sink)
        {
            return;
        }

        sink(new Text((byte)TextKind.Features, Features(annotating)));
        if (primary is { } screen)
        {
            sink(new Text(
                (byte)TextKind.ScreenSize,
                string.Create(CultureInfo.InvariantCulture, $"{screen.Bounds.Width}/{screen.Bounds.Height}")));
        }
    }

    private bool Apply(ActionId id)
    {
        switch (id)
        {
            case ActionId.AnnotateBegin:
                board.Begin(BoardMode.Annotate);
                return true;
            case ActionId.SignBegin:
                board.Begin(BoardMode.Sign);
                return true;
            case ActionId.AnnotateEnd:
                // Ends a signature too: the phone has one way out of either surface.
                board.End();
                return true;
            case ActionId.AnnotateUndo:
                return board.Undo();
            case ActionId.AnnotateClear:
                return board.Clear();
            case ActionId.ToolPen:
                return board.SetTool(StrokeTool.Pen);
            case ActionId.ToolHighlighter:
                return board.SetTool(StrokeTool.Highlighter);
            case ActionId.ToolLine:
                return board.SetTool(StrokeTool.Line);
            case ActionId.ToolArrow:
                return board.SetTool(StrokeTool.Arrow);
            case ActionId.ToolRectangle:
                return board.SetTool(StrokeTool.Rectangle);
            case ActionId.ToolEllipse:
                return board.SetTool(StrokeTool.Ellipse);
            default:
                return false;
        }
    }

    private bool SaveSignature()
    {
        Stroke[] strokes;
        lock (gate)
        {
            if (board.Mode != BoardMode.Sign)
            {
                return false;
            }

            // Finished strokes never change again, so the copy can share them.
            strokes = [.. board.Strokes];
        }

        // Answered here, on the reader like the pad's status, when there is nothing to render or nowhere to render
        // it: no ink means no file and no clipboard, and with no UI thread the export would never run.
        if (strokes.Length == 0 || !uiUp)
        {
            send?.Invoke(new Text((byte)TextKind.SignStatus, strokes.Length == 0 ? SignStatus.Empty : SignStatus.Failed));
            return true;
        }

        // Rendering needs Avalonia's renderer and the clipboard its UI thread.
        Post(() => _ = ExportAsync(strokes));
        return true;
    }

    /// <summary>
    /// Asks the UI thread for a repaint. One post in flight at a time: a finger sends a point every few milliseconds
    /// and only the newest board is worth drawing.
    /// </summary>
    private void Redraw()
    {
        if (Interlocked.Exchange(ref posted, 1) == 0)
        {
            Post(Drain);
        }
    }

    /// <summary>
    /// Draws the newest board on the UI thread, or takes the overlay down when there is nothing to show. The gate is
    /// cleared first, so a point that lands while this draws wins a post of its own.
    /// </summary>
    private void Drain()
    {
        Volatile.Write(ref posted, 0);
        if (Volatile.Read(ref disposed) != 0 || Snapshot() is not { } strokes)
        {
            Remove();
            return;
        }

        if (!annotating || overlay is not { } window || primary is not { } screen)
        {
            return;
        }

        try
        {
            window.Present(strokes, screen.Bounds);
            owner = this;
        }
        catch (Exception e)
        {
            // Logged once: annotate is off from here on, and every phone is told so.
            Log.Write($"The annotation overlay could not be shown, so annotate is turned off: {e}");
            annotating = false;
            owner = null;
            Changed?.Invoke();

            // Last: if the half-shown window cannot even be hidden, the post's own catch logs that.
            window.Withdraw();
        }
    }

    /// <summary>What the overlay should show, or null when it should not be up.</summary>
    private Stroke[]? Snapshot()
    {
        lock (gate)
        {
            if (board.Mode != BoardMode.Annotate)
            {
                return null;
            }

            // The stroke under the finger is still growing on the reader, so it alone is copied.
            if (board.Active is not { } active)
            {
                return [.. board.Strokes];
            }

            var copy = new Stroke(active.Tool, active.Colour);
            copy.Points.AddRange(active.Points);
            return [.. board.Strokes, copy];
        }
    }

    /// <summary>Hides the overlay if it is showing this session's strokes. UI thread.</summary>
    private void Remove()
    {
        if (owner != this)
        {
            return;
        }

        owner = null;
        overlay?.Withdraw();
    }

    /// <summary>The export, on the UI thread. Every outcome is answered, so the phone never waits on nothing.</summary>
    private async Task ExportAsync(Stroke[] strokes)
    {
        // The session ended while this was queued: its strokes are discarded, and there is no one to answer.
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        string status;
        try
        {
            status = await SaveAsync(strokes);
        }
        catch (Exception e)
        {
            Log.Write($"A signature could not be saved: {e.Message}");
            status = SignStatus.Failed;
        }

        // The reply is a socket write, and one to a phone that has stopped reading blocks: not on the UI thread.
        var sink = send;
        _ = Task.Run(() =>
        {
            try
            {
                sink?.Invoke(new Text((byte)TextKind.SignStatus, status));
            }
            catch (Exception e)
            {
                Log.Write($"The signature status could not be sent: {e.Message}");
            }
        });
    }

    private static async Task<string> SaveAsync(Stroke[] strokes)
    {
        if (primary is not { } screen)
        {
            Log.Write("A signature could not be saved: the screen size is not known");
            return SignStatus.Failed;
        }

        if (SignatureExport.Render(strokes, screen.Bounds.Width, screen.Bounds.Height) is not { } png)
        {
            return SignStatus.Empty;
        }

        var path = SignatureExport.WriteNew(SignatureExport.Folder(), SignatureExport.Stem(DateTime.Now), png);
        Log.Write($"Signature saved to {path}");
        if (!await SignatureExport.CopyAsync(overlay, png))
        {
            Log.Write("The signature was saved but could not be put on the clipboard");
            return SignStatus.Failed;
        }

        return SignStatus.Saved;
    }

    /// <summary>The primary screen's bounds, in physical pixels.</summary>
    private sealed record PrimaryScreen(PixelRect Bounds);
}
