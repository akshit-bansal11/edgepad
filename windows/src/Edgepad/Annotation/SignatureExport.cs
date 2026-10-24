using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using Edgepad.Protocol;
using DrawingPen = System.Drawing.Pen; // Edgepad.Protocol.Pen, the PEN frame, is in scope here too

namespace Edgepad.Annotation;

/// <summary>
/// Turns finished strokes into the PNG a signature is pasted from: transparent, at screen resolution, cropped to
/// the ink so it drops into a document at a sensible size.
/// </summary>
internal static class SignatureExport
{
    /// <summary>Breathing room around the ink, in pixels, so the stroke's anti-aliased edge is never clipped.</summary>
    internal const int Margin = 8;

    /// <summary>
    /// The part of a <paramref name="width"/> by <paramref name="height"/> screen that the ink and its margin
    /// cover; null when there is no ink.
    /// </summary>
    public static Rectangle? CropRect(IReadOnlyList<Stroke> strokes, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        var any = false;
        foreach (var stroke in strokes)
        {
            if (stroke.Ink(width, height) is not { } ink)
            {
                continue;
            }

            min = Vector2.Min(min, ink.Min);
            max = Vector2.Max(max, ink.Max);
            any = true;
        }

        if (!any)
        {
            return null;
        }

        var crop = Rectangle.Intersect(
            Rectangle.FromLTRB(
                (int)MathF.Floor(min.X) - Margin,
                (int)MathF.Floor(min.Y) - Margin,
                (int)MathF.Ceiling(max.X) + Margin,
                (int)MathF.Ceiling(max.Y) + Margin),
            new Rectangle(0, 0, width, height));
        return crop.IsEmpty ? null : crop;
    }

    /// <summary>The PNG, or null when there is nothing to export.</summary>
    public static byte[]? Render(IReadOnlyList<Stroke> strokes, int width, int height)
    {
        if (CropRect(strokes, width, height) is not { } crop)
        {
            return null;
        }

        // Only the crop is allocated: a 4K screen is 33 MB of pixels and almost all of them would be empty.
        using var bitmap = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.TranslateTransform(-crop.X, -crop.Y);
            foreach (var stroke in strokes)
            {
                Draw(g, stroke, width, height);
            }
        }

        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    /// <summary>One stroke as it looks on a <paramref name="width"/> by <paramref name="height"/> screen; the overlay draws with it too.</summary>
    internal static void Draw(Graphics g, Stroke stroke, int width, int height)
    {
        var points = stroke.Path(width, height).Select(p => new PointF(p.X, p.Y)).ToArray();
        if (points.Length == 0)
        {
            return;
        }

        var alpha = stroke.Tool == StrokeTool.Highlighter ? (int)Math.Round(255 * Stroke.HighlighterAlpha) : 255;
        var colour = Color.FromArgb(alpha, Color.FromArgb(Palette.Colour(stroke.Colour) ?? 0));
        var size = stroke.WidthPx(height);

        // A tap is a dot, not nothing: a line of no length draws no pixels. Down and up on one spot is two
        // identical points, so it is the points being all alike that marks a tap.
        if (points.All(p => p == points[0]))
        {
            using var dot = new SolidBrush(colour);
            g.FillEllipse(dot, points[0].X - (size / 2f), points[0].Y - (size / 2f), size, size);
            return;
        }

        // One path, one stroke: overlapping stretches of a translucent highlighter must not darken twice.
        using var path = new GraphicsPath();
        if (stroke.Tool is StrokeTool.Rectangle or StrokeTool.Ellipse)
        {
            var box = RectangleF.FromLTRB(
                Math.Min(points[0].X, points[1].X),
                Math.Min(points[0].Y, points[1].Y),
                Math.Max(points[0].X, points[1].X),
                Math.Max(points[0].Y, points[1].Y));
            if (stroke.Tool == StrokeTool.Rectangle)
            {
                path.AddRectangle(box);
            }
            else
            {
                path.AddEllipse(box);
            }
        }
        else
        {
            path.AddLines(points);
            var wings = stroke.ArrowWings(width, height);
            if (wings.Length == 2)
            {
                path.StartFigure();
                path.AddLines([new PointF(wings[0].X, wings[0].Y), points[1], new PointF(wings[1].X, wings[1].Y)]);
            }
        }

        using var pen = new DrawingPen(colour, size)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawPath(pen, path);
    }
}
