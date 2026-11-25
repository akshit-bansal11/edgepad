using System.ComponentModel;
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

    /// <summary>The monitor chosen when annotating or signing began, null when neither is open. Under <see cref="gate"/>.</summary>
    private Rectangle? chosen;

    private int disposed;
    private int posted;

    /// <summary>The screen-rate redraw, running only while a laser shows. UI thread only; built on first use.</summary>
    private System.Windows.Forms.Timer? laserTick;

    /// <summary>
    /// Where the answer to SIGN_SAVE goes. Set by the session, which owns the socket, for the reason
    /// <see cref="Dispatch.Dispatcher.PadStatusReply"/> is.
    /// </summary>
    public Action<string>? SignStatusReply { get; set; }

    /// <summary>
    /// Raised on the session thread when beginning or ending annotation changes the size of the display the
    /// layer is on, so the session can send TEXT 10 again. Set by the session, which owns the socket.
    /// </summary>
    public Action? ScreenChanged { get; set; }

    /// <summary>The primary display in physical pixels (the app is PerMonitorV2).</summary>
    public static Rectangle PrimaryBounds() =>
        Screen.PrimaryScreen?.Bounds ?? new Rectangle(Point.Empty, SystemInformation.PrimaryMonitorSize);

    /// <summary>
    /// The display the layer draws on and signatures are sized to: the monitor under the cursor when annotating
    /// or signing began, held until it ends, else the primary. A phone's 0..65535 span maps to this one screen.
    /// </summary>
    public Rectangle Bounds()
    {
        lock (gate)
        {
            return chosen ?? PrimaryBounds();
        }
    }

    /// <summary>Follows a resolution change of the chosen monitor. Called when the display settings change.</summary>
    public void Refresh()
    {
        lock (gate)
        {
            if (chosen is { } was)
            {
                chosen = Screen.FromRectangle(was).Bounds;
            }
        }
    }

    private static Rectangle UnderCursor()
    {
        try
        {
            return Screen.FromPoint(Cursor.Position).Bounds;
        }
        catch (Win32Exception)
        {
            // No cursor to read, a locked desktop say: the primary screen is as good a guess as any.
            return PrimaryBounds();
        }
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

    /// <summary>The colour of the next stroke from PEN_RGB, already held to six hex digits by the dispatcher.</summary>
    public bool SetRgb(int rgb)
    {
        lock (gate)
        {
            return board.SetRgb(rgb);
        }
    }

    /// <summary>The width of the next stroke, 1..100. False when it is not.</summary>
    public bool SetWidth(byte width)
    {
        lock (gate)
        {
            return board.SetWidth(width);
        }
    }

    /// <summary>The opacity of the next stroke in percent, 10..100. False when it is not.</summary>
    public bool SetOpacity(byte opacity)
    {
        lock (gate)
        {
            return board.SetOpacity(opacity);
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
        bool resized;
        lock (gate)
        {
            var before = chosen ?? PrimaryBounds();
            taken = Apply(id);
            resized = taken && (chosen ?? PrimaryBounds()).Size != before.Size;
        }

        if (resized)
        {
            ScreenChanged?.Invoke();
        }

        if (taken)
        {
            Redraw();
        }

        return taken;
    }

    /// <summary>
    /// The board's own action, and the screen it is on: a begin takes the screen under the cursor, and an end lets it
    /// go. The caller holds the lock, which covers both.
    /// </summary>
    private bool Apply(ActionId id)
    {
        chosen = id switch
        {
            ActionId.AnnotateBegin or ActionId.SignBegin => UnderCursor(),
            ActionId.AnnotateEnd => null,
            _ => chosen,
        };
        return board.Apply(id);
    }

    private bool SaveSignature()
    {
        Stroke[] strokes;
        Rectangle screen;
        lock (gate)
        {
            if (board.Mode != BoardMode.Sign)
            {
                return false;
            }

            // Finished strokes never change again, so the copy can share them. The bounds are the ones SIGN_BEGIN
            // chose, taken now: an ANNOTATE_END queued behind this clears them before the export runs.
            strokes = [.. board.Strokes];
            screen = chosen ?? PrimaryBounds();
        }

        if (strokes.Length == 0)
        {
            // Answered here, on the session thread like the pad's status, and before anything is rendered or
            // written: there is no ink, so there is no file and no clipboard to touch.
            SignStatusReply?.Invoke(SignStatus.Empty);
            return true;
        }

        // The clipboard is an OLE call and needs an STA thread, which the session thread is not.
        Post(() => Export(strokes, screen));
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
        var (strokes, laser, live) = Volatile.Read(ref disposed) != 0 ? (null, null, false) : Snapshot();
        if (strokes is null)
        {
            Remove();
            return;
        }

        SetLaserTick(live);
        Present(strokes, laser);
    }

    /// <summary>
    /// What the overlay should show: the strokes (null when it should not be up), the laser's frame, and whether a
    /// laser still shows anything.
    /// </summary>
    private (Stroke[]? Strokes, LaserFrame? Laser, bool Live) Snapshot()
    {
        var screen = Bounds();
        lock (gate)
        {
            if (board.Mode != BoardMode.Annotate)
            {
                return (null, null, false);
            }

            // The stroke under the finger is still growing on the session thread, so it alone is copied.
            if (board.Active is not { } active)
            {
                return ([.. board.Strokes], null, false);
            }

            if (active.IsLaser)
            {
                var now = board.NowMs;
                return ([.. board.Strokes], LaserFrame.Of(active, now, screen.Width, screen.Height), board.LaserVisible(now));
            }

            var copy = new Stroke(active.Tool, active.Style);
            copy.Points.AddRange(active.Points);
            return ([.. board.Strokes, copy], null, false);
        }
    }

    /// <summary>Runs the 16 ms redraw timer exactly while a laser shows, then stops it. UI thread.</summary>
    private void SetLaserTick(bool live)
    {
        if (!live)
        {
            laserTick?.Stop();
            return;
        }

        if (laserTick is null)
        {
            laserTick = new System.Windows.Forms.Timer { Interval = 16 };
            laserTick.Tick += (_, _) => Redraw();
        }

        laserTick.Start();
    }

    private void Present(Stroke[] strokes, LaserFrame? laser)
    {
        if (stopped)
        {
            return;
        }

        try
        {
            window ??= new AnnotationOverlay();
            window.Present(strokes, laser, Bounds());
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
        laserTick?.Stop();
        var open = window;
        window = null;
        open?.Dispose();
    }

    /// <summary>The export, on the UI thread. Every outcome is answered, so the phone never waits on nothing.</summary>
    private void Export(Stroke[] strokes, Rectangle screen)
    {
        // The session ended while this was queued: its strokes are discarded, and there is no one to answer.
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        string status;
        try
        {
            status = Save(strokes, screen);
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

    private static string Save(Stroke[] strokes, Rectangle screen)
    {
        if (SignatureExport.Render(strokes, screen.Width, screen.Height) is not { } png)
        {
            return SignStatus.Empty;
        }

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Edgepad");
        SignatureFile.WriteNew(folder, SignatureFile.Stem(DateTime.Now), png);

        // The file is written, so the signature is saved whatever the clipboard does: another app holding it open
        // must not make the phone say the save failed.
        try
        {
            // PNG keeps the transparency for the apps that read it; the bitmap is for the many that only take that.
            // Copied, so the clipboard holds its own rendering once these are disposed.
            using var stream = new MemoryStream(png);
            using var image = new Bitmap(stream);
            using var pngData = new MemoryStream(png);
            var data = new DataObject();
            data.SetData("PNG", false, pngData);
            data.SetImage(image);
            Clipboard.SetDataObject(data, true, 5, 100);
        }
        catch (Exception e)
        {
            Log.Write($"A saved signature could not be put on the clipboard: {e.Message}");
        }

        return SignStatus.Saved;
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
            chosen = null;
            board.End();
        }

        // Always a post, even from the UI thread: the window is only ever touched there, and a drain still
        // queued runs first and sees the session gone.
        Post(() =>
        {
            laserTick?.Dispose();
            laserTick = null;
            window?.Dispose();
            window = null;
        });
    }
}
