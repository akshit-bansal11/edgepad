using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Edgepad.Annotation;
using Edgepad.Native;

namespace Edgepad.Ui;

/// <summary>
/// The layer the strokes are drawn on: borderless, topmost, over the whole primary screen, click-through and never
/// activated, so the owner keeps working under the ink. Avalonia runs on X11 on Linux (XWayland under a Wayland
/// session), so this is an X11 window: Topmost is _NET_WM_STATE_ABOVE (Avalonia's X11Window.SetTopmost sets exactly
/// that atom, so it is not set again here), the transparency is an ARGB visual that needs a compositor, and the
/// click-through is an empty input shape set through <see cref="X11"/>. Built once, hidden, and only ever touched on
/// the UI thread.
/// </summary>
internal sealed class AnnotationOverlay : Window
{
    private readonly Layer layer = new();

    public AnnotationOverlay()
    {
        Title = LinuxStrings.Annotate_OverlayTitle;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = layer;
    }

    /// <summary>
    /// Makes the window click-through and checks it will be see-through, before it is ever shown, so it never once
    /// covers the screen opaque or eats a click. Null when it can be used, else why not.
    /// </summary>
    public string? Prepare()
    {
        var handle = TryGetPlatformHandle();
        return Annotator.Refusal(
            handle?.HandleDescriptor,
            ActualTransparencyLevel == WindowTransparencyLevel.Transparent,
            () => handle is not null && X11.ClickThrough((nuint)handle.Handle));
    }

    /// <summary>
    /// Draws <paramref name="strokes"/> over <paramref name="bounds"/> (the primary screen, in physical pixels) and
    /// shows the window if it is not up yet. Bounds are set every time, so a resolution change is followed.
    /// </summary>
    public void Present(IReadOnlyList<Stroke> strokes, PixelRect bounds)
    {
        layer.Paint(strokes, bounds.Size);
        if (Position != bounds.Position)
        {
            Position = bounds.Position;
        }

        // The window's own scaling is the one Avalonia turns these into pixels with.
        Width = bounds.Width / RenderScaling;
        Height = bounds.Height / RenderScaling;
        if (!IsVisible)
        {
            Show();
        }
    }

    /// <summary>Hides the window and lets go of its strokes.</summary>
    public void Withdraw()
    {
        layer.Paint([], default);
        Hide();
    }

    /// <summary>Draws the strokes in screen pixels, scaled back to the window's units.</summary>
    private sealed class Layer : Control
    {
        private const double LaserGlowRadii = 2.2;
        private static readonly Color LaserColour = Color.FromRgb(0xFF, 0x1A, 0x1A);
        private static readonly ImmutableSolidColorBrush LaserBrush = new(LaserColour);
        private static readonly ImmutableSolidColorBrush LaserHotBrush = new(Color.FromRgb(0xFF, 0xD0, 0xD0));

        private IReadOnlyList<Stroke> strokes = [];
        private PixelSize screen;
        private Point[] edge = new Point[64];

        /// <summary>
        /// A laser: a hot core inside a soft red glow, always red whatever the pen's colour, sized by the pen's width.
        /// The trailing one also draws a red tail that fades and thins away behind the dot.
        /// </summary>
        private void DrawLaser(DrawingContext context, Stroke stroke)
        {
            var path = stroke.Path(screen.Width, screen.Height);
            if (path.Length == 0)
            {
                return;
            }

            var dot = new Point(path[^1].X, path[^1].Y);
            var radius = stroke.LaserRadiusPx(screen.Height);

            DrawTail(context, path, radius);

            var glow = new RadialGradientBrush
            {
                GradientStops =
                [
                    new GradientStop(Color.FromArgb(140, 0xFF, 0x1A, 0x1A), 0),
                    new GradientStop(Color.FromArgb(0, 0xFF, 0x1A, 0x1A), 1),
                ],
            };
            context.DrawEllipse(glow, null, dot, radius * LaserGlowRadii, radius * LaserGlowRadii);
            context.DrawEllipse(LaserBrush, null, dot, radius, radius);
            context.DrawEllipse(LaserHotBrush, null, dot, radius * 0.35, radius * 0.35);
        }

        /// <summary>
        /// The trail as one smooth ribbon: as wide as the dot at the head, tapering to nothing at the tail, and
        /// fading with a single gradient, so no joints or dots show. The edge buffer is reused every frame.
        /// </summary>
        private void DrawTail(DrawingContext context, System.Numerics.Vector2[] path, double radius)
        {
            var n = path.Length;
            if (n < 3 || (path[n - 1] - path[0]).LengthSquared() < 1f)
            {
                return;
            }

            var count = n * 2;
            if (edge.Length < count)
            {
                edge = new Point[count];
            }

            var (nx, ny) = (0.0, 1.0);
            for (var i = 0; i < n; i++)
            {
                var a = path[Math.Max(i - 1, 0)];
                var b = path[Math.Min(i + 1, n - 1)];
                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                var length = Math.Sqrt((dx * dx) + (dy * dy));
                if (length > 0.001)
                {
                    (nx, ny) = (-dy / length, dx / length);
                }

                var half = radius * i / (n - 1);
                edge[i] = new Point(path[i].X + (nx * half), path[i].Y + (ny * half));
                edge[count - 1 - i] = new Point(path[i].X - (nx * half), path[i].Y - (ny * half));
            }

            // A closed loop of quadratic curves through the edge points' midpoints: smooth, no corners but the tail.
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(Mid(edge[count - 1], edge[0]), true);
                for (var i = 0; i < count; i++)
                {
                    ctx.QuadraticBezierTo(edge[i], Mid(edge[i], edge[(i + 1) % count]));
                }

                ctx.EndFigure(true);
            }

            var fade = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(path[0].X, path[0].Y, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(path[n - 1].X, path[n - 1].Y, RelativeUnit.Absolute),
                GradientStops =
                [
                    new GradientStop(Color.FromArgb(0, 0xFF, 0x1A, 0x1A), 0),
                    new GradientStop(Color.FromArgb(220, 0xFF, 0x1A, 0x1A), 1),
                ],
            };
            context.DrawGeometry(fade, null, geometry);
        }

        private static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        public void Paint(IReadOnlyList<Stroke> next, PixelSize size)
        {
            strokes = next;
            screen = size;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var scale = 1 / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            {
                foreach (var stroke in strokes)
                {
                    if (stroke.IsLaser)
                    {
                        DrawLaser(context, stroke);
                    }
                    else
                    {
                        SignatureExport.Draw(context, stroke, screen.Width, screen.Height);
                    }
                }
            }
        }
    }
}
