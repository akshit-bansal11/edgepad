using System.Numerics;

namespace Edgepad.Annotation;

/// <summary>
/// Where a signature is cut from the screen: the box the ink covers, with <see cref="Margin"/> pixels around it, held
/// to the screen. Each laptop app's <c>SignatureExport</c> turns the answer into its own rectangle type and renders
/// that part, so the Windows and Linux PNGs are cut to the same pixel.
/// </summary>
internal static class SignatureCrop
{
    /// <summary>Breathing room around the ink, in pixels, so the stroke's anti-aliased edge is never clipped.</summary>
    internal const int Margin = 8;

    /// <summary>
    /// The part of a <paramref name="width"/> by <paramref name="height"/> screen that the ink and its margin cover,
    /// as left, top, width and height in pixels; null when the screen has no size, there is no ink, or what is left
    /// after holding it to the screen is empty.
    /// </summary>
    public static (int X, int Y, int Width, int Height)? Of(IReadOnlyList<Stroke> strokes, int width, int height)
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

        var left = Math.Max(0, (int)MathF.Floor(min.X) - Margin);
        var top = Math.Max(0, (int)MathF.Floor(min.Y) - Margin);
        var right = Math.Min(width, (int)MathF.Ceiling(max.X) + Margin);
        var bottom = Math.Min(height, (int)MathF.Ceiling(max.Y) + Margin);
        return right > left && bottom > top ? (left, top, right - left, bottom - top) : null;
    }
}
