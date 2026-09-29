using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using Edgepad.Ui;

namespace Edgepad.Controls;

/// <summary>The level a readout is about, which decides both its word and its icon.</summary>
internal enum LevelKind
{
    Volume,
    Microphone,
    Brightness,
}

/// <summary>
/// The on-screen readout of what the phone just changed. Windows shows nothing of its own here: the level is
/// set through Core Audio and WMI rather than by pressing a media key, so this is the only feedback the
/// laptop gives. The window is built once and then only moved, repainted and re-timed — a dial drag arrives
/// as dozens of frames a second, and building a window per frame would cost far more than drawing one.
/// </summary>
internal sealed class LevelOverlay : IDisposable
{
    /// <summary>
    /// The thread the window lives on. Captured at construction, which happens on the UI thread; every
    /// <see cref="Show"/> arrives from a Bluetooth thread and is posted back here. Posting rather than
    /// sending is deliberate — the receive loop must never block on the message pump.
    /// </summary>
    private readonly SynchronizationContext ui;

    /// <summary>Presents that may fail in a row before the overlay stops trying.</summary>
    private const int MaxFailures = 3;

    private OverlayWindow? window;

    /// <summary>Presents that have failed since the last one that worked. Only touched on the UI thread.</summary>
    private int failures;

    /// <summary>Set once the overlay is disposed, or once building it has failed and is not worth retrying.</summary>
    private volatile bool stopped;

    public LevelOverlay() => ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

    /// <summary>
    /// The newest reading waiting to be drawn; only the last one before the UI catches up matters. A record
    /// class rather than a struct because Volatile's read and write are only defined over references, and
    /// four fields could not be published atomically anyway.
    /// </summary>
    private sealed record Reading(LevelKind Kind, string Caption, int Level, bool Muted);

    private Reading? latest;
    private int posted;

    /// <summary>Shows or refreshes the overlay. percent 0-100; muted draws the muted form.</summary>
    public void Show(LevelKind kind, int percent, bool muted = false)
    {
        if (stopped)
        {
            return;
        }

        // Shaped here, on the caller's thread, so the UI thread only ever paints.
        var caption = Metrics.Caption(percent, muted);
        var level = Math.Clamp(percent, 0, 100);

        // One post in flight at a time. A dial drag sends a frame per step from a thread running above
        // normal priority, and each Present relayouts and invalidates on a UI thread that cannot keep up:
        // the message queue would grow without bound while the panel only ever shows the newest value
        // anyway. Dropping the ones in between is what the overlay would have done visually regardless.
        Volatile.Write(ref latest, new Reading(kind, caption, level, muted));
        if (Interlocked.Exchange(ref posted, 1) == 0)
        {
            ui.Post(_ => Drain(), null);
        }
    }

    /// <summary>
    /// Draws whatever the newest reading is, on the UI thread. The gate is cleared first, not last, so a
    /// reading that lands while this is drawing wins a post of its own rather than being stranded until
    /// the next frame arrives — the cost is an occasional repaint of a value already on screen.
    /// </summary>
    private void Drain()
    {
        Volatile.Write(ref posted, 0);
        if (Volatile.Read(ref latest) is { } reading)
        {
            Present(reading.Kind, reading.Caption, reading.Level, reading.Muted);
        }
    }

    private void Present(LevelKind kind, string caption, int percent, bool muted)
    {
        if (stopped)
        {
            return;
        }

        try
        {
            window ??= new OverlayWindow();
            window.Present(kind, caption, percent, muted);
            failures = 0;
        }
        catch (Exception e)
        {
            // Nothing here is load-bearing: the phone has already changed the level, this only says so. One
            // failure — a monitor unplugged mid-drag, a display switching mode — throws the window away and the
            // next reading builds a fresh one; it used to switch the readout off until the app restarted. Only a
            // setup that fails MaxFailures times running is given up on, rather than logging the same failure
            // on every frame of every drag.
            var broken = window;
            window = null;
            broken?.Dispose();
            if (++failures < MaxFailures)
            {
                Log.Write($"Level overlay failed, rebuilding it: {e.Message}");
            }
            else
            {
                Log.Write($"Level overlay disabled after {MaxFailures} failures in a row: {e}");
                stopped = true;
            }
        }
    }

    public void Dispose()
    {
        stopped = true;
        if (window is null)
        {
            return;
        }

        // Shutdown runs on the UI thread, where this disposes inline. From anywhere else the pump may already
        // be gone, so the window is handed back to its own thread and not waited on.
        if (window.InvokeRequired)
        {
            var open = window;
            open.BeginInvoke(() => open.Dispose());
        }
        else
        {
            window.Dispose();
        }

        window = null;
    }

    /// <summary>
    /// The arithmetic and the wording, with no window behind them. Split out because everything else in this
    /// file needs a desktop to run at all.
    ///
    /// Called Metrics rather than Layout on purpose: OverlayWindow derives from Form, which inherits a
    /// Control.Layout event, and inside that class an unqualified Layout binds to the event rather than to
    /// a nested type. Every use of it there is then a compile error about the left side of +=.
    /// </summary>
    internal static class Metrics
    {
        /// <summary>Sentence case, as every label in the 2.0 design is.</summary>
        public static string Label(LevelKind kind) => kind switch
        {
            LevelKind.Microphone => "Microphone",
            LevelKind.Brightness => "Brightness",
            _ => "Volume",
        };

        /// <summary>The bare number: the bar under it already says it is a share of the whole.</summary>
        public static string Caption(int percent, bool muted) =>
            muted ? "Muted" : Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture);

        public static int Scale(int value, int dpi) => Theme.Scale(value, dpi);

        /// <summary>
        /// Lato's capital and figure height as a share of its em (1433 of 2000 units): how far the value's digits
        /// rise above their baseline, which is where the panel's top padding is measured to.
        /// </summary>
        public const float CapShare = 0.7165f;

        /// <summary>
        /// The panel's height for <paramref name="pad"/> on every side of the content: from the top of the value's
        /// digits, <paramref name="cap"/> above its baseline, down <paramref name="baselineToBar"/> to the bar and
        /// through its <paramref name="bar"/> height. The sides use the same pad, so the gap is equal all round.
        /// </summary>
        public static int PanelHeight(int pad, float cap, int baselineToBar, int bar) =>
            (int)MathF.Round((2 * pad) + cap + baselineToBar + bar);

        public static int BarWidth(int track, int percent) =>
            (int)Math.Round(track * Math.Clamp(percent, 0, 100) / 100.0);

        /// <summary>
        /// Bottom-centre of the working area, <paramref name="margin"/> above its lower edge. The working area
        /// rather than the bounds keeps the panel clear of the taskbar wherever it is docked.
        /// </summary>
        public static Point Anchor(Rectangle work, Size panel, int margin) =>
            new(work.Left + ((work.Width - panel.Width) / 2), work.Bottom - panel.Height - margin);
    }

    /// <summary>
    /// The window itself: a borderless, click-through, never-activated pill that paints an icon, a label, a value
    /// and a bar, then fades out.
    /// </summary>
    private sealed class OverlayWindow : Form
    {
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;

        /// <summary>GenericTypographic's flag set, spelled out so no undisposed StringFormat is created to read it.</summary>
        private const StringFormatFlags Typographic =
            StringFormatFlags.FitBlackBox | StringFormatFlags.LineLimit | StringFormatFlags.NoClip;

        // Measurements at 96 dpi, from the design; Metrics.Scale turns each into pixels for the monitor in front
        // of the user.
        private const int PanelWidth = 280;
        private const int Corner = 18;

        // The same on all four sides: to the icon and the value at the sides, to the digits' tops and the bar's
        // foot above and below. The height follows from it rather than being fixed, so it cannot drift apart.
        private const int Pad = 18;
        private const int IconSize = 22;
        private const int Gap = 14;
        private const int BaselineToBar = 12;
        private const int BarHeight = 6;
        private const int LabelSize = 13;
        private const int ValueSize = 20;
        private const int BottomMargin = 72;

        /// <summary>
        /// The pill's opacity at rest. The design's panel is 88% opaque over a blur; WinForms has no per-pixel
        /// alpha or backdrop blur for a plain window, so the whole window takes the alpha instead, text and all.
        /// </summary>
        private const double Solid = 0.88;

        /// <summary>How long the panel stays up after the last frame, and how much of that tail it spends fading.</summary>
        private const long VisibleMs = 1200;
        private const long FadeMs = 220;

        /// <summary>
        /// The overlay lies over whatever is already on screen, so it is always the dark form — there is no
        /// surface to match.
        /// </summary>
        private static readonly Palette Colours = Palette.Dark;

        private readonly System.Windows.Forms.Timer clock = new() { Interval = 25 };
        private readonly StringFormat typographic = new(Typographic);

        /// <summary>The dpi the shape was last built for; rebuilding it per frame would not be cheap.</summary>
        private int laidOutAt = 96;

        private LevelKind kind;
        private string caption = string.Empty;
        private int percent;
        private bool muted;
        private long hideAt;

        public OverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Colours.Card;
            DoubleBuffered = true;
            Opacity = Solid;

            // WinForms' own scaling would fight the pixel measurements above; every size here comes from
            // Metrics.Scale and DeviceDpi instead.
            AutoScaleMode = AutoScaleMode.None;

            clock.Tick += OnTick;
        }

        /// <summary>
        /// Never take the foreground. WS_EX_NOACTIVATE keeps the click that shows the panel from moving focus,
        /// WS_EX_TRANSPARENT lets the mouse reach whatever is underneath, and WS_EX_TOOLWINDOW keeps it out of
        /// Alt-Tab and the taskbar.
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WsExNoActivate | WsExTransparent | WsExToolWindow;
                return parameters;
            }
        }

        /// <summary>The other half of not stealing focus: WinForms shows this with SW_SHOWNOACTIVATE.</summary>
        protected override bool ShowWithoutActivation => true;

        public void Present(LevelKind level, string value, int fill, bool silenced)
        {
            kind = level;
            caption = value;
            percent = fill;
            muted = silenced;
            hideAt = Environment.TickCount64 + VisibleMs;

            Relayout();
            if (!Visible)
            {
                Show();
            }
            else if (Opacity < Solid)
            {
                // A frame arriving mid-fade pulls the panel back to full rather than starting a second one.
                Opacity = Solid;
            }

            Invalidate();
            clock.Start();
        }

        /// <summary>
        /// Sizes and places the panel for the primary screen's current dpi. Cheap on the frames that change
        /// nothing: the rounded shape is rebuilt only when the bounds actually move.
        /// </summary>
        private void Relayout()
        {
            var dpi = DeviceDpi;
            var work = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
            var height = Metrics.PanelHeight(
                Metrics.Scale(Pad, dpi),
                Metrics.Scale(ValueSize, dpi) * Metrics.CapShare,
                Metrics.Scale(BaselineToBar, dpi),
                Metrics.Scale(BarHeight, dpi));
            var size = new Size(Metrics.Scale(PanelWidth, dpi), height);
            var where = new Rectangle(Metrics.Anchor(work, size, Metrics.Scale(BottomMargin, dpi)), size);
            laidOutAt = dpi;

            if (Bounds == where)
            {
                return;
            }

            Bounds = where;

            // The rounded corners are a window region rather than a painted shape, so the desktop shows through
            // them. Control.Region disposes the one it replaces.
            using var shape = Theme.Rounded(new RectangleF(0, 0, size.Width, size.Height), Metrics.Scale(Corner, dpi));
            Region = new Region(shape);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Relayout();
            Invalidate();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            var left = hideAt - Environment.TickCount64;
            if (left <= 0)
            {
                clock.Stop();

                // Back to rest before hiding, so the next Present starts from the plain form rather than
                // inheriting the tail of this fade.
                Opacity = Solid;
                Hide();
                return;
            }

            if (left < FadeMs)
            {
                Opacity = Solid * left / FadeMs;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Grid-fit rather than ClearType: the panel is layered, where subpixel edges fringe.
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            var dpi = laidOutAt;
            using (var hairline = new Pen(Colours.Line))
            using (var outline = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), Metrics.Scale(Corner, dpi)))
            {
                graphics.DrawPath(hairline, outline);
            }

            var pad = Metrics.Scale(Pad, dpi);
            var icon = Metrics.Scale(IconSize, dpi);
            var glyph = kind switch
            {
                LevelKind.Microphone => Icons.Microphone,
                LevelKind.Brightness => Icons.Sun,
                _ when muted => Icons.VolumeMuted,
                _ => Icons.Volume,
            };
            Icons.Draw(graphics, glyph, new RectangleF(pad, (Height - icon) / 2f, icon, icon), muted ? Colours.Dim : Colours.Accent);

            var left = pad + icon + Metrics.Scale(Gap, dpi);
            var right = Width - pad;
            var labelFont = Theme.Font(Metrics.Scale(LabelSize, dpi), Weight.Bold);
            var valueFont = Theme.Font(Metrics.Scale(ValueSize, dpi), Weight.Black);
            var bar = Metrics.Scale(BarHeight, dpi);

            // Label and value share a baseline, as the design's flex row aligns them, set so the digits' tops
            // sit exactly one pad below the panel's top.
            var baseline = pad + (valueFont.Size * Metrics.CapShare);
            var top = baseline - Ascent(valueFont);
            using (var dim = new SolidBrush(Colours.Dim))
            {
                graphics.DrawString(Metrics.Label(kind), labelFont, dim, left, baseline - Ascent(labelFont), typographic);
            }

            var width = graphics.MeasureString(caption, valueFont, PointF.Empty, typographic).Width;
            using (var ink = new SolidBrush(Colours.Ink))
            {
                graphics.DrawString(caption, valueFont, ink, right - width, top, typographic);
            }

            var track = new RectangleF(left, baseline + Metrics.Scale(BaselineToBar, dpi), right - left, bar);
            using (var off = new SolidBrush(Colours.Off))
            using (var shape = Theme.Rounded(track, bar / 2f))
            {
                graphics.FillPath(off, shape);
            }

            var filled = muted ? 0 : Metrics.BarWidth((int)track.Width, percent);
            if (filled > 0)
            {
                using var accent = new SolidBrush(Colours.Accent);
                using var shape = Theme.Rounded(track with { Width = filled }, bar / 2f);
                graphics.FillPath(accent, shape);
            }
        }

        /// <summary>A pixel font's ascent in pixels, which is where its baseline sits below the top it is drawn at.</summary>
        private static float Ascent(Font font)
        {
            var family = font.FontFamily;
            return font.Size * family.GetCellAscent(font.Style) / family.GetEmHeight(font.Style);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                clock.Stop();
                clock.Dispose();
                typographic.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
