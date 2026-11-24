using System.Drawing.Drawing2D;

namespace Edgepad.Ui;

/// <summary>
/// The Edgepad logo, drawn here rather than loaded, that plays its 0.75 s pop once when the window it sits in opens
/// (<see cref="LogoMotion"/>). Decorative: no tab stop and no name. The timer runs only while the pop plays; with
/// Windows animations off the finished logo is drawn straight away and no timer starts.
/// </summary>
internal sealed class LogoMark : Control
{
    private readonly Palette palette;
    private readonly System.Windows.Forms.Timer frames = new() { Interval = 16 };
    private long started;
    private double ms;

    public LogoMark(Palette palette)
    {
        this.palette = palette;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        BackColor = palette.Card;
        ms = Theme.Motion.Reveal == 0 ? LogoMotion.TotalMs : 0;
        frames.Tick += (_, _) => Step();
    }

    /// <summary>Plays the pop from the start; a no-op while Windows has animations off, where the logo already stands finished.</summary>
    public void Play()
    {
        if (Theme.Motion.Reveal == 0)
        {
            return;
        }

        ms = 0;
        started = Environment.TickCount64;
        frames.Start();
        Invalidate();
    }

    private void Step()
    {
        ms = Math.Min(LogoMotion.TotalMs, Environment.TickCount64 - started);
        if (ms >= LogoMotion.TotalMs)
        {
            frames.Stop();
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var side = Math.Min(Width, Height);
        var unit = (float)(side / LogoMotion.Canvas);

        using (var tile = Tile(unit))
        using (var fill = new SolidBrush(palette.Card))
        using (var edge = new Pen(palette.Hairline))
        {
            g.FillPath(fill, tile);
            g.DrawPath(edge, tile);
        }

        var poses = LogoMotion.Frame(ms);
        for (var i = 0; i < poses.Length; i++)
        {
            var s = LogoMotion.Strokes[i];
            var alpha = (int)Math.Round(255 * poses[i].Opacity);
            if (alpha == 0)
            {
                continue;
            }

            using var pen = new Pen(Color.FromArgb(alpha, s.Accent ? palette.Accent : palette.Ink), (float)(LogoMotion.StrokeWidth * unit))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            var k = poses[i].Scale;
            if (s.Arc)
            {
                var r = (float)(s.Outer * k * unit);
                var c = (float)(LogoMotion.Pivot * unit);
                g.DrawArc(pen, c - r, c - r, 2 * r, 2 * r, 180, 90);
                continue;
            }

            var radians = s.Angle * Math.PI / 180;
            PointF At(double radius) =>
                new(
                    (float)((LogoMotion.Pivot + (radius * k * Math.Cos(radians))) * unit),
                    (float)((LogoMotion.Pivot + (radius * k * Math.Sin(radians))) * unit));
            g.DrawLine(pen, At(s.Inner), At(s.Outer));
        }
    }

    /// <summary>The logo's own tile outline (continuous corners), from lines and cubic Béziers, scaled by <paramref name="unit"/>.</summary>
    private static GraphicsPath Tile(float unit)
    {
        PointF At(double x, double y) => new((float)(x * unit), (float)(y * unit));

        var path = new GraphicsPath();
        var at = At(LogoMotion.TileStart.X, LogoMotion.TileStart.Y);
        foreach (var s in LogoMotion.TileSegments)
        {
            if (s.Length == 2)
            {
                var to = At(s[0], s[1]);
                path.AddLine(at, to);
                at = to;
            }
            else
            {
                var end = At(s[4], s[5]);
                path.AddBezier(at, At(s[0], s[1]), At(s[2], s[3]), end);
                at = end;
            }
        }

        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            frames.Dispose();
        }

        base.Dispose(disposing);
    }
}
