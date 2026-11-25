using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Edgepad.Protocol;
using DrawingPen = System.Drawing.Pen; // Edgepad.Protocol.Pen, the PEN frame, is in scope here too

namespace Edgepad.Annotation;

/// <summary>
/// Turns finished strokes into the PNG a signature is pasted from: transparent, at screen resolution, cropped to
/// the ink so it drops into a document at a sensible size.
/// </summary>
internal static class SignatureExport
{
    /// <summary>
    /// The part of a <paramref name="width"/> by <paramref name="height"/> screen that the ink and its margin cover;
    /// null when there is no ink. See <see cref="SignatureCrop.Of"/>.
    /// </summary>
    public static Rectangle? CropRect(IReadOnlyList<Stroke> strokes, int width, int height) =>
        SignatureCrop.Of(strokes, width, height) is { } c ? new Rectangle(c.X, c.Y, c.Width, c.Height) : null;

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

        // The stroke's own colour, width and opacity, so the signature saved is the one the phone's sliders showed.
        var colour = Color.FromArgb(stroke.Alpha, Color.FromArgb(stroke.Rgb));
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
