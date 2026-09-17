using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace Edgepad.Controls;

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

    private OverlayWindow? window;

    /// <summary>Set once the overlay is disposed, or once building it has failed and is not worth retrying.</summary>
    private volatile bool stopped;

    public LevelOverlay() => ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

    /// <summary>Shows or refreshes the overlay. label e.g. "VOLUME"; percent 0-100; muted draws the muted form.</summary>
    public void Show(string label, int percent, bool muted = false)
    {
        if (stopped)
        {
            return;
        }

        // Shaped here, on the caller's thread, so the UI thread only ever paints.
        var heading = Metrics.Label(label);
        var caption = Metrics.Caption(percent, muted);
        var level = Math.Clamp(percent, 0, 100);
        ui.Post(_ => Present(heading, caption, level, muted), null);
    }

    private void Present(string label, string caption, int percent, bool muted)
    {
        if (stopped)
        {
            return;
        }

        try
        {
            window ??= new OverlayWindow();
            window.Present(label, caption, percent, muted);
        }
        catch (Exception e)
        {
            // Nothing here is load-bearing: the phone has already changed the level, this only says so. A
            // display setup that cannot host the window switches the readout off for the session rather than
            // logging the same failure on every frame of the next drag.
            Log.Write($"Level overlay disabled: {e}");
            stopped = true;
            var broken = window;
            window = null;
            broken?.Dispose();
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
    /// </summary>
    /// <summary>
    /// The pure arithmetic and wording, apart from any window so it can be tested without one.
    ///
    /// Called Metrics rather than Layout on purpose: OverlayWindow derives from Form, which inherits a
    /// Control.Layout event, and inside that class an unqualified Layout binds to the event rather than to
    /// a nested type. Every use of it there is then a compile error about the left side of +=.
    /// </summary>
    internal static class Metrics
    {
        /// <summary>The design's baseline: every measurement in this file is written for 96 dpi and scaled from it.</summary>
        private const double BaselineDpi = 96.0;

        public static string Label(string label) =>
            string.IsNullOrWhiteSpace(label) ? string.Empty : label.Trim().ToUpperInvariant();

        public static string Caption(int percent, bool muted) =>
            muted ? "MUTED" : Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture) + "%";

        public static int Scale(int value, int dpi) => (int)Math.Round(value * dpi / BaselineDpi);

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
    /// The window itself: a borderless, click-through, never-activated panel that paints a label, a value and
    /// a bar, then fades out.
    /// </summary>
    private sealed class OverlayWindow : Form
    {
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;

        /// <summary>GenericTypographic's flag set, spelled out so no undisposed StringFormat is created to read it.</summary>
        private const StringFormatFlags Typographic =
            StringFormatFlags.FitBlackBox | StringFormatFlags.LineLimit | StringFormatFlags.NoClip;

        /// <summary>One face everywhere, as on the phone. A missing family falls back inside GDI+ rather than throwing.</summary>
        private const string Face = "Consolas";

        // Measurements at 96 dpi; Metrics.Scale turns each into pixels for the monitor in front of the user.
        private const int PanelWidth = 248;
        private const int PanelHeight = 76;
        private const int Corner = 14;
        private const int Pad = 18;
        private const int BarHeight = 3;
        private const int LabelSize = 12;
        private const int ValueSize = 20;
        private const int Tracking = 2;
        private const int BottomMargin = 88;

        /// <summary>How long the panel stays up after the last frame, and how much of that tail it spends fading.</summary>
        private const long VisibleMs = 1200;
        private const long FadeMs = 220;

        // The dark set from the Android palette (android/.../values-night/colors.xml). The overlay lies over
        // whatever is already on screen, so it is always the dark form — there is no surface to match.
        private static readonly Color PanelColour = Color.FromArgb(0x00, 0x00, 0x00);
        private static readonly Color LineColour = Color.FromArgb(0x29, 0x29, 0x29);
        private static readonly Color InkColour = Color.FromArgb(0xFF, 0xFF, 0xFF);
        private static readonly Color DimColour = Color.FromArgb(0x8C, 0x8C, 0x8C);

        private readonly System.Windows.Forms.Timer clock = new() { Interval = 25 };
        private readonly StringFormat charCell = new(Typographic);
        private readonly StringFormat rightAligned = new(Typographic) { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

        private Font labelFont;
        private Font valueFont;

        /// <summary>The dpi the fonts and the shape were last built for; rebuilding them per frame would not be cheap.</summary>
        private int laidOutAt = 96;

        private string label = string.Empty;
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
            BackColor = PanelColour;
            DoubleBuffered = true;

            // WinForms' own scaling would fight the pixel measurements above; every size here comes from
            // Metrics.Scale and DeviceDpi instead.
            AutoScaleMode = AutoScaleMode.None;

            labelFont = new Font(Face, LabelSize, FontStyle.Regular, GraphicsUnit.Pixel);
            valueFont = new Font(Face, ValueSize, FontStyle.Regular, GraphicsUnit.Pixel);

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

        public void Present(string heading, string value, int level, bool silenced)
        {
            label = heading;
            caption = value;
            percent = level;
            muted = silenced;
            hideAt = Environment.TickCount64 + VisibleMs;

            Relayout();
            if (!Visible)
            {
                Show();
            }
            else if (Opacity < 1d)
            {
                // A frame arriving mid-fade pulls the panel back to full rather than starting a second one.
                Opacity = 1d;
            }

            Invalidate();
            clock.Start();
        }

        /// <summary>
        /// Sizes and places the panel for the primary screen's current dpi. Cheap on the frames that change
        /// nothing: the fonts and the rounded shape are rebuilt only when the dpi actually moves.
        /// </summary>
        private void Relayout()
        {
            var dpi = DeviceDpi;
            var work = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
            var size = new Size(Metrics.Scale(PanelWidth, dpi), Metrics.Scale(PanelHeight, dpi));
            var where = new Rectangle(Metrics.Anchor(work, size, Metrics.Scale(BottomMargin, dpi)), size);

            if (dpi != laidOutAt)
            {
                laidOutAt = dpi;
                labelFont.Dispose();
                valueFont.Dispose();
                labelFont = new Font(Face, (float)Metrics.Scale(LabelSize, dpi), FontStyle.Regular, GraphicsUnit.Pixel);
                valueFont = new Font(Face, (float)Metrics.Scale(ValueSize, dpi), FontStyle.Regular, GraphicsUnit.Pixel);
            }

            if (Bounds == where)
            {
                return;
            }

            Bounds = where;

            // The rounded corners are a window region rather than a painted shape, so the desktop shows through
            // them. Control.Region disposes the one it replaces.
            using var shape = RoundedPath(size, Metrics.Scale(Corner, dpi));
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

                // Back to opaque before hiding: Opacity below 1 makes the window layered, and the next Present
                // should start from the plain form rather than inherit the tail of this fade.
                Opacity = 1d;
                Hide();
                return;
            }

            if (left < FadeMs)
            {
                Opacity = (double)left / FadeMs;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Grid-fit rather than ClearType: the panel goes layered during the fade, where subpixel edges fringe.
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            var pad = Metrics.Scale(Pad, laidOutAt);
            var inner = Width - (pad * 2);

            using var hairline = new Pen(LineColour);
            using var outline = RoundedPath(new Size(Width - 1, Height - 1), Metrics.Scale(Corner, laidOutAt));
            graphics.DrawPath(hairline, outline);

            using var ink = new SolidBrush(InkColour);
            using var dim = new SolidBrush(DimColour);
            Brush accent = muted ? dim : ink;

            // Label and value share a row: the name on the left, tracked wide and dim, the number on the right.
            var row = new Rectangle(pad, pad, inner, valueFont.Height + 2);
            DrawTracked(graphics, label, dim, pad, row.Top + ((row.Height - labelFont.Height) / 2f));
            graphics.DrawString(caption, valueFont, accent, row, rightAligned);

            var thickness = Metrics.Scale(BarHeight, laidOutAt);
            var track = new Rectangle(pad, Height - pad - thickness, inner, thickness);
            using var trackBrush = new SolidBrush(LineColour);
            graphics.FillRectangle(trackBrush, track);
            graphics.FillRectangle(accent, track.X, track.Y, Metrics.BarWidth(inner, percent), thickness);
        }

        /// <summary>
        /// Draws one character at a time to fake letter-spacing, which GDI+ has no setting for. The face is
        /// monospaced, so a single measurement gives every character's advance.
        /// </summary>
        private void DrawTracked(Graphics graphics, string text, Brush brush, float x, float y)
        {
            if (text.Length == 0)
            {
                return;
            }

            var advance = graphics.MeasureString("M", labelFont, PointF.Empty, charCell).Width + Metrics.Scale(Tracking, laidOutAt);
            foreach (var character in text)
            {
                graphics.DrawString(character.ToString(), labelFont, brush, x, y, charCell);
                x += advance;
            }
        }

        private static GraphicsPath RoundedPath(Size size, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(0, 0, diameter, diameter, 180f, 90f);
            path.AddArc(size.Width - diameter, 0, diameter, diameter, 270f, 90f);
            path.AddArc(size.Width - diameter, size.Height - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(0, size.Height - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                clock.Stop();
                clock.Dispose();
                labelFont.Dispose();
                valueFont.Dispose();
                charCell.Dispose();
                rightAligned.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
