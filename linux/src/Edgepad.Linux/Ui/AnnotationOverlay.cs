using System.Numerics;
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
    /// Draws <paramref name="strokes"/> and the <paramref name="laser"/> over <paramref name="bounds"/> (the primary
    /// screen, in physical pixels) and shows the window if it is not up yet. Bounds are set every time, so a
    /// resolution change is followed.
    /// </summary>
    public void Present(IReadOnlyList<Stroke> strokes, LaserFrame? laser, PixelRect bounds)
    {
        layer.Paint(strokes, laser, bounds.Size);
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
        layer.Paint([], null, default);
        Hide();
    }

    /// <summary>Draws the strokes in screen pixels, scaled back to the window's units.</summary>
    private sealed class Layer : Control
    {
        private static readonly ImmutableSolidColorBrush LaserBrush = new(Color.FromRgb(0xFF, 0x1A, 0x1A));
        private static readonly ImmutableSolidColorBrush LaserHotBrush = new(Color.FromRgb(0xFF, 0xF4, 0xF0));

        private static readonly RadialGradientBrush LaserGlowBrush = new()
        {
            GradientStops =
            [
                new GradientStop(Color.FromArgb(140, 0xFF, 0x1A, 0x1A), 0),
                new GradientStop(Color.FromArgb(0, 0xFF, 0x1A, 0x1A), 1),
            ],
        };

        private IReadOnlyList<Stroke> strokes = [];
        private LaserFrame? laser;
        private PixelSize screen;

        /// <summary>
        /// The laser: a solid red core with a white-hot centre inside a soft glow, always red whatever the pen's
        /// colour. The trail is one filled outline, alpha fading from tail to head; it is never stroked as segments.
        /// </summary>
        private static void DrawLaser(DrawingContext context, LaserFrame frame)
        {
            using var faded = context.PushOpacity(frame.Visibility);
            var dot = new Point(frame.Dot.X, frame.Dot.Y);
            var radius = frame.Radius;

            if (frame.Outline.Length >= 3)
            {
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(frame.Outline[0].X, frame.Outline[0].Y), true);
                    for (var i = 1; i < frame.Outline.Length; i++)
                    {
                        ctx.LineTo(new Point(frame.Outline[i].X, frame.Outline[i].Y));
                    }

                    ctx.EndFigure(true);
                }

                var fade = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(frame.Tail.X, frame.Tail.Y, RelativeUnit.Absolute),
                    EndPoint = new RelativePoint(dot, RelativeUnit.Absolute),
                    GradientStops =
                    [
                        new GradientStop(Color.FromArgb(frame.TailAlpha, 0xFF, 0x1A, 0x1A), 0),
                        new GradientStop(Color.FromArgb(frame.HeadAlpha, 0xFF, 0x1A, 0x1A), 1),
                    ],
                };
                context.DrawGeometry(fade, null, geometry);
            }

            var glow = radius * Stroke.LaserGlow;
            context.DrawEllipse(LaserGlowBrush, null, dot, glow, glow);
            context.DrawEllipse(LaserBrush, null, dot, radius, radius);
            context.DrawEllipse(LaserHotBrush, null, dot, radius * 0.35, radius * 0.35);
        }

        public void Paint(IReadOnlyList<Stroke> next, LaserFrame? nextLaser, PixelSize size)
        {
            strokes = next;
            laser = nextLaser;
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
                    SignatureExport.Draw(context, stroke, screen.Width, screen.Height);
                }

                if (laser is { } frame)
                {
                    DrawLaser(context, frame);
                }
            }
        }
    }
}

/// <summary>One laser's picture for one frame, taken under the board's lock: dot, size, fade, and the trail's outline.</summary>
internal sealed record LaserFrame(Vector2 Dot, float Radius, float Visibility, Vector2[] Outline, Vector2 Tail, byte TailAlpha, byte HeadAlpha);
