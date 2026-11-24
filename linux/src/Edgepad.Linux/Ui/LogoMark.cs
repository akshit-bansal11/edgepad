using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Edgepad.Ui;

/// <summary>
/// The Edgepad logo, drawn here rather than loaded, that plays its 0.75 s pop once each time it joins a window
/// (<see cref="LogoMotion"/>). Decorative: hidden from the accessibility tree, never focused. The timer runs only
/// while the pop plays; with the desktop's animations off the finished logo is drawn at once.
/// </summary>
internal sealed class LogoMark : Control
{
    private const double TileRadius = 230;
    private readonly Look look = Look.Current;
    private readonly DispatcherTimer frames = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private long started;
    private double ms = LogoMotion.TotalMs;

    public LogoMark(double size)
    {
        Width = size;
        Height = size;
        Focusable = false;
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        frames.Tick += (_, _) => Step();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!Motion.Enabled)
        {
            return;
        }

        ms = 0;
        started = Environment.TickCount64;
        frames.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        frames.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Step()
    {
        ms = Math.Min(LogoMotion.TotalMs, Environment.TickCount64 - started);
        if (ms >= LogoMotion.TotalMs)
        {
            frames.Stop();
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        var unit = side / LogoMotion.Canvas;
        var radius = TileRadius * unit;
        context.DrawRectangle(look.Card, new Pen(look.Line), new Rect(0.5, 0.5, side - 1, side - 1), radius, radius);

        var poses = LogoMotion.Frame(ms);
        for (var i = 0; i < poses.Length; i++)
        {
            var s = LogoMotion.Strokes[i];
            var k = poses[i].Scale;
            var pen = new Pen(s.Accent ? look.Accent : look.Ink, LogoMotion.StrokeWidth * unit, lineCap: PenLineCap.Round);
            using (context.PushOpacity(poses[i].Opacity))
            {
                if (s.Arc)
                {
                    var r = s.Outer * k * unit;
                    var c = LogoMotion.Pivot * unit;
                    var arc = new StreamGeometry();
                    using (var g = arc.Open())
                    {
                        g.BeginFigure(new Point(c - r, c), false);
                        g.ArcTo(new Point(c, c - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
                        g.EndFigure(false);
                    }

                    context.DrawGeometry(null, pen, arc);
                    continue;
                }

                var radians = s.Angle * Math.PI / 180;
                Point At(double reach) =>
                    new(
                        (LogoMotion.Pivot + (reach * k * Math.Cos(radians))) * unit,
                        (LogoMotion.Pivot + (reach * k * Math.Sin(radians))) * unit);
                context.DrawLine(pen, At(s.Inner), At(s.Outer));
            }
        }
    }
}
