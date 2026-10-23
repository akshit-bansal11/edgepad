using System.Globalization;
using Edgepad.Protocol;
using Pen = Edgepad.Protocol.Pen; // System.Drawing.Pen is in scope too, through the WinForms implicit usings

namespace Edgepad.Annotation;

/// <summary>
/// One connection's annotate and sign sessions: the strokes, the overlay that shows them, and the signature
/// export. Frames arrive on the session thread and change the board there, under a lock, so each one can be
/// counted as taken or dropped at once; the window lives on the UI thread and only ever reads a copy.
/// </summary>
internal sealed class Annotator(SynchronizationContext ui) : IDisposable
{
    /// <summary>Presents that may fail in a row before the overlay stops trying, as for the level readout.</summary>
    private const int MaxFailures = 3;

    private readonly Lock gate = new();
    private readonly StrokeBoard board = new();

    /// <summary>Only touched on the UI thread.</summary>
    private AnnotationOverlay? window;

    /// <summary>Presents that have failed since the last one that worked. Only touched on the UI thread.</summary>
    private int failures;

    /// <summary>Set once building the overlay has failed and is not worth retrying. Only touched on the UI thread.</summary>
    private bool stopped;

    private int disposed;
    private int posted;

    /// <summary>
    /// Where the answer to SIGN_SAVE goes. Set by the session, which owns the socket, for the reason
    /// <see cref="Dispatch.Dispatcher.PadStatusReply"/> is.
    /// </summary>
    public Action<string>? SignStatusReply { get; set; }

    /// <summary>
    /// The primary display in physical pixels (the app is PerMonitorV2), which is the only one drawn on: a
    /// phone's 0..65535 span maps to one screen, and which one is not something it could choose.
    /// </summary>
    public static Rectangle PrimaryBounds() =>
        Screen.PrimaryScreen?.Bounds ?? new Rectangle(Point.Empty, SystemInformation.PrimaryMonitorSize);

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

        if (strokes.Length == 0)
        {
            // Answered here, on the session thread like the pad's status, and before anything is rendered or
            // written: there is no ink, so there is no file and no clipboard to touch.
            SignStatusReply?.Invoke(SignStatus.Empty);
            return true;
        }

        // The clipboard is an OLE call and needs an STA thread, which the session thread is not.
        Post(() => Export(strokes));
        return true;
    }

    /// <summary>
    /// Asks the UI thread for a repaint. One post in flight at a time, as in the level readout: a finger sends
    /// a point every few milliseconds from a thread above normal priority, a full-screen repaint is slower than
    /// that, and only the newest board is worth drawing anyway.
    /// </summary>
    private void Redraw()
    {
        if (Interlocked.Exchange(ref posted, 1) == 0)
        {
            Post(Drain);
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the UI thread. Nothing may escape a post: it would end the tray app from
    /// inside its message loop.
    /// </summary>
    private void Post(Action work)
    {
        try
        {
            ui.Post(
                _ =>
                {
                    try
                    {
                        work();
                    }
                    catch (Exception e)
                    {
                        Log.Write($"Annotation work on the UI thread failed: {e}");
                    }
                },
                null);
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            // The message loop has gone, so Edgepad is quitting, and the window goes with the process.
        }
    }

    /// <summary>
    /// Draws the newest board on the UI thread, or takes the overlay down when there is nothing to show. The gate
    /// is cleared first, so a point that lands while this draws wins a post of its own.
    /// </summary>
    private void Drain()
    {
        Volatile.Write(ref posted, 0);
        if (Volatile.Read(ref disposed) != 0 || Snapshot() is not { } strokes)
        {
            Remove();
            return;
        }

        Present(strokes);
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

            // The stroke under the finger is still growing on the session thread, so it alone is copied.
            if (board.Active is not { } active)
            {
                return [.. board.Strokes];
            }

            var copy = new Stroke(active.Tool, active.Colour);
            copy.Points.AddRange(active.Points);
            return [.. board.Strokes, copy];
        }
    }

    private void Present(Stroke[] strokes)
    {
        if (stopped)
        {
            return;
        }

        try
        {
            window ??= new AnnotationOverlay();
            window.Present(strokes);
            failures = 0;
        }
        catch (Exception e)
        {
            // The level readout's policy: a monitor unplugged mid-stroke throws the window away and the next point
            // builds a fresh one, and only a setup that fails MaxFailures times running is given up on.
            Remove();
            if (++failures < MaxFailures)
            {
                Log.Write($"Annotation overlay failed, rebuilding it: {e.Message}");
            }
            else
            {
                Log.Write($"Annotation overlay disabled after {MaxFailures} failures in a row: {e}");
                stopped = true;
            }
        }
    }

    private void Remove()
    {
        var open = window;
        window = null;
        open?.Dispose();
    }

    /// <summary>The export, on the UI thread. Every outcome is answered, so the phone never waits on nothing.</summary>
    private void Export(Stroke[] strokes)
    {
        // The session ended while this was queued: its strokes are discarded, and there is no one to answer.
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        string status;
        try
        {
            status = Save(strokes);
        }
        catch (Exception e)
        {
            Log.Write($"A signature could not be saved: {e.Message}");
            status = SignStatus.Failed;
        }

        // The reply is a socket write, and one to a phone that has stopped reading blocks: not on the UI thread.
        var reply = SignStatusReply;
        _ = Task.Run(() =>
        {
            try
            {
                reply?.Invoke(status);
            }
            catch (Exception e)
            {
                // A dead link is the read loop's to notice and end; a lost status costs nothing more.
                Log.Write($"The signature status could not be sent: {e.Message}");
            }
        });
    }

    private static string Save(Stroke[] strokes)
    {
        var screen = PrimaryBounds();
        if (SignatureExport.Render(strokes, screen.Width, screen.Height) is not { } png)
        {
            return SignStatus.Empty;
        }

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Edgepad");
        var stem = string.Create(CultureInfo.InvariantCulture, $"signature-{DateTime.Now:yyyyMMdd-HHmmss}");
        WriteNew(folder, stem, png);

        // PNG keeps the transparency for the apps that read it; the bitmap is for the many that only take that.
        // Copied, so the clipboard holds its own rendering once these are disposed.
        using var stream = new MemoryStream(png);
        using var image = new Bitmap(stream);
        using var pngData = new MemoryStream(png);
        var data = new DataObject();
        data.SetData("PNG", false, pngData);
        data.SetImage(image);
        Clipboard.SetDataObject(data, true, 5, 100);
        return SignStatus.Saved;
    }

    /// <summary>
    /// Writes <paramref name="png"/> as <c>stem.png</c> in <paramref name="folder"/>, or <c>stem-2.png</c>,
    /// <c>stem-3.png</c> and on when that is taken, and returns the path. Never over an existing file: two saves
    /// in one second share a stem, and overwriting would lose the first signature. CreateNew rather than a check
    /// first, so a file that appears between the check and the write cannot be overwritten either.
    /// </summary>
    internal static string WriteNew(string folder, string stem, byte[] png)
    {
        Directory.CreateDirectory(folder);
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? $"{stem}.png" : string.Create(CultureInfo.InvariantCulture, $"{stem}-{n}.png");
            var path = Path.Combine(folder, name);
            FileStream file;
            try
            {
                file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Taken: try the next number. Only the open is caught, so a failed write (a full disk) still
                // fails the save rather than spinning through names.
                continue;
            }

            using (file)
            {
                file.Write(png);
            }

            return path;
        }
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

        lock (gate)
        {
            board.End();
        }

        // Always a post, even from the UI thread: the window is only ever touched there, and a drain still
        // queued runs first and sees the session gone.
        Post(() =>
        {
            window?.Dispose();
            window = null;
        });
    }
}
