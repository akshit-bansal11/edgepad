using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using Edgepad.Injection;

namespace Edgepad.Annotation;

/// <summary>
/// The layer the strokes are drawn on: borderless, topmost, over the whole primary monitor, click-through and
/// never activated, so the owner keeps working under the ink. It is a per-pixel-alpha layered window drawn with
/// UpdateLayeredWindow, because WinForms' Opacity and TransparencyKey are all-or-nothing per window or per
/// colour and cannot show the highlighter's partial alpha over what is underneath.
/// </summary>
internal sealed class AnnotationOverlay : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;

    private Canvas? canvas;

    public AnnotationOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;

        // The layer is sized in pixels from the screen's own bounds; WinForms scaling would only fight that.
        AutoScaleMode = AutoScaleMode.None;
    }

    /// <summary>
    /// WS_EX_LAYERED for the per-pixel alpha, and the level readout's three for the rest: WS_EX_TRANSPARENT lets
    /// every click reach the window underneath, WS_EX_NOACTIVATE keeps focus where it was, and WS_EX_TOOLWINDOW
    /// keeps the layer out of Alt-Tab and the taskbar.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow;
            return parameters;
        }
    }

    /// <summary>The other half of not stealing focus: WinForms shows this with SW_SHOWNOACTIVATE.</summary>
    protected override bool ShowWithoutActivation => true;

    /// <summary>Repaints the whole layer with <paramref name="strokes"/> and the laser, and shows it if it is not up yet.</summary>
    public void Present(IReadOnlyList<Stroke> strokes, LaserFrame? laser, Rectangle screen)
    {
        if (canvas is null || canvas.Size != screen.Size)
        {
            canvas?.Dispose();
            canvas = null;
            canvas = new Canvas(screen.Size);
        }

        canvas.Draw(strokes, laser);
        canvas.Push(Handle, screen.Location);
        if (!Visible)
        {
            // Pushed first, so the layer appears with its ink rather than as one empty frame.
            Bounds = screen;
            Show();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            canvas?.Dispose();
            canvas = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// A screen-sized DIB section held for the life of the layer, with a GDI+ bitmap over the same memory. Each
    /// repaint draws straight into the pixels UpdateLayeredWindow reads, so a stroke costs a clear and a redraw
    /// rather than a fresh 8 MB (33 MB at 4K) bitmap per point.
    /// </summary>
    private sealed class Canvas : IDisposable
    {
        private const uint DibRgbColours = 0;
        private const byte AcSrcOver = 0;
        private const byte AcSrcAlpha = 1;
        private const uint UlwAlpha = 2;

        /// <summary>The glow reaches this many radii out from the dot's centre.</summary>
        private const float LaserGlowRadii = Stroke.LaserGlow;

        /// <summary>The white-hot centre's radius as a share of the core's.</summary>
        private const float HotCentre = 0.35f;

        private static readonly Color CoreColour = Color.FromArgb(0xFF, 0xFF, 0x1A, 0x1A);
        private static readonly Color HotColour = Color.FromArgb(0xFF, 0xFF, 0xD0, 0xD0);

        private readonly nint dc;
        private readonly nint dib;
        private readonly nint previous;
        private readonly Bitmap bitmap;

        public Canvas(Size size)
        {
            Size = size;
            dc = NativeMethods.CreateCompatibleDC(0);
            if (dc == 0)
            {
                throw new InvalidOperationException("CreateCompatibleDC failed");
            }

            // A negative height is a top-down DIB: row 0 is the top, as GDI+ expects of the memory it is handed.
            var header = new BitmapInfoHeader(size.Width, -size.Height);
            dib = NativeMethods.CreateDIBSection(dc, in header, DibRgbColours, out var bits, 0, 0);
            if (dib == 0)
            {
                _ = NativeMethods.DeleteDC(dc);
                throw new InvalidOperationException($"CreateDIBSection failed for {size.Width}x{size.Height}");
            }

            previous = NativeMethods.SelectObject(dc, dib);

            // Premultiplied, which is what UpdateLayeredWindow's per-pixel alpha takes, so GDI+ writes it as is.
            bitmap = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, bits);
        }

        public Size Size { get; }

        public void Draw(IReadOnlyList<Stroke> strokes, LaserFrame? laser)
        {
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            foreach (var stroke in strokes)
            {
                if (!stroke.IsLaser)
                {
                    SignatureExport.Draw(g, stroke, Size.Width, Size.Height);
                }
            }

            if (laser is not null)
            {
                DrawLaser(g, laser);
            }
        }

        /// <summary>
        /// A laser: a red core with a white-hot centre inside a soft glow, always red whatever the pen's colour. The
        /// trail is one filled outline with a tail-to-head alpha gradient, never stroked as segments, so no seams
        /// show. Every alpha is scaled by the frame's visibility, which is how a lost pen-up fades out.
        /// </summary>
        private static void DrawLaser(Graphics g, LaserFrame frame)
        {
            var radius = frame.Radius;
            var point = new PointF(frame.Dot.X, frame.Dot.Y);

            // A tail on the dot has no direction for the gradient to run along.
            if (frame.Outline.Length >= 3 && (frame.Dot - frame.Tail).LengthSquared() >= 1f)
            {
                var polygon = new PointF[frame.Outline.Length];
                for (var i = 0; i < polygon.Length; i++)
                {
                    polygon[i] = new PointF(frame.Outline[i].X, frame.Outline[i].Y);
                }

                using var fade = new LinearGradientBrush(
                    new PointF(frame.Tail.X, frame.Tail.Y),
                    point,
                    Faded(CoreColour, frame.TailAlpha, frame.Visibility),
                    Faded(CoreColour, frame.HeadAlpha, frame.Visibility));
                g.FillPolygon(fade, polygon, FillMode.Winding);
            }

            var glow = radius * LaserGlowRadii;
            using (var disc = new GraphicsPath())
            {
                disc.AddEllipse(point.X - glow, point.Y - glow, glow * 2f, glow * 2f);
                using var brush = new PathGradientBrush(disc)
                {
                    CenterColor = Faded(CoreColour, 140, frame.Visibility),
                    SurroundColors = [Faded(CoreColour, 0, frame.Visibility)],
                };
                g.FillPath(brush, disc);
            }

            using var core = new SolidBrush(Faded(CoreColour, 0xFF, frame.Visibility));
            g.FillEllipse(core, point.X - radius, point.Y - radius, radius * 2f, radius * 2f);
            var hot = radius * HotCentre;
            using var centre = new SolidBrush(Faded(HotColour, 0xFF, frame.Visibility));
            g.FillEllipse(centre, point.X - hot, point.Y - hot, hot * 2f, hot * 2f);
        }

        private static Color Faded(Color colour, int alpha, float visibility) =>
            Color.FromArgb((int)Math.Round(alpha * visibility), colour.R, colour.G, colour.B);

        public void Push(nint window, Point at)
        {
            var position = new NativePoint(at.X, at.Y);
            var size = new NativeSize(Size.Width, Size.Height);
            var origin = new NativePoint(0, 0);
            var blend = new BlendFunction(AcSrcOver, 255, AcSrcAlpha);
            if (!NativeMethods.UpdateLayeredWindow(window, 0, in position, in size, dc, in origin, 0, in blend, UlwAlpha))
            {
                throw new Win32Exception();
            }
        }

        public void Dispose()
        {
            bitmap.Dispose();
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.DeleteObject(dib);
            _ = NativeMethods.DeleteDC(dc);
        }
    }
}

/// <summary>One laser's picture for one frame, taken under the board's lock: dot, size, fade, and the trail's outline.</summary>
internal sealed record LaserFrame(Vector2 Dot, float Radius, float Visibility, Vector2[] Outline, Vector2 Tail, byte TailAlpha, byte HeadAlpha)
{
    /// <summary>The laser's picture at <paramref name="now"/>, or null once it has faded out or has no point.</summary>
    public static LaserFrame? Of(Stroke stroke, long now, int width, int height)
    {
        var visibility = LaserGeometry.Visibility(now, stroke.LastEventMs);
        if (visibility <= 0f)
        {
            return null;
        }

        var path = stroke.Path(width, height);
        if (path.Length == 0)
        {
            return null;
        }

        var radius = stroke.LaserRadiusPx(height);
        var (points, ages) = stroke.Trail(width, height, now);
        var outline = LaserGeometry.Outline(points, ages, radius);
        var head = (byte)Math.Round(255f * LaserGeometry.TrailAlpha(0f));
        var tail = (byte)Math.Round(255f * LaserGeometry.TrailAlpha(ages.Length > 0 ? ages[0] : 0f));
        return new LaserFrame(path[^1], radius, visibility, outline, points.Length > 0 ? points[0] : path[^1], tail, head);
    }
}
