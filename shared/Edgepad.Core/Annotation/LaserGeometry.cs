using System.Numerics;

namespace Edgepad.Annotation;

/// <summary>
/// The laser's pure maths: how visible it is, and the one closed outline its trail is filled with. No window and
/// no clock of its own, so a test drives it and the three overlays share it.
/// </summary>
internal static class LaserGeometry
{
    /// <summary>No pen event for this long and the laser starts to fade: a lost pen-up cannot leave a dot behind.</summary>
    public const long WatchdogMs = 400;

    /// <summary>The fade from fully shown to gone.</summary>
    public const long FadeMs = 150;

    /// <summary>Samples per trail segment after smoothing.</summary>
    internal const int Subdivisions = 4;

    /// <summary>1 until <see cref="WatchdogMs"/> after the last event, then linearly to 0 over <see cref="FadeMs"/>.</summary>
    public static float Visibility(long nowMs, long lastEventMs)
    {
        var silent = nowMs - lastEventMs;
        if (silent <= WatchdogMs)
        {
            return 1f;
        }

        return silent >= WatchdogMs + FadeMs ? 0f : 1f - ((silent - WatchdogMs) / (float)FadeMs);
    }

    /// <summary>True once nothing shows any more: the stroke may be dropped and the redraw timer stopped.</summary>
    public static bool IsFinished(long nowMs, long lastEventMs) => Visibility(nowMs, lastEventMs) <= 0f;

    /// <summary>The trail's alpha 0..1 at <paramref name="ageMs"/>: 0.9 at the head, 0 at <see cref="Stroke.TrailWindowMs"/>.</summary>
    public static float TrailAlpha(float ageMs) =>
        0.9f * Math.Clamp(1f - (ageMs / Stroke.TrailWindowMs), 0f, 1f);

    /// <summary>
    /// The trail as one closed polygon: the left edge from the tail to the head, then the right edge back. The
    /// half-width is <paramref name="radius"/> at age 0 (the head) and 0 at <see cref="Stroke.TrailWindowMs"/>
    /// (the tail), and the centre line is smoothed through the points with a Catmull-Rom spline. Fewer than two
    /// points have no direction, so no outline. <paramref name="points"/> and <paramref name="ages"/> are oldest first.
    /// </summary>
    public static Vector2[] Outline(IReadOnlyList<Vector2> points, IReadOnlyList<float> ages, float radius)
    {
        if (points.Count < 2 || ages.Count != points.Count)
        {
            return [];
        }

        var centre = new List<Vector2>();
        var age = new List<float>();
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[Math.Max(i - 1, 0)];
            var p3 = points[Math.Min(i + 2, points.Count - 1)];
            for (var s = 0; s < Subdivisions; s++)
            {
                var t = s / (float)Subdivisions;
                centre.Add(Vector2.CatmullRom(p0, points[i], points[i + 1], p3, t));
                age.Add(ages[i] + ((ages[i + 1] - ages[i]) * t));
            }
        }

        centre.Add(points[^1]);
        age.Add(ages[^1]);

        var left = new Vector2[centre.Count];
        var right = new Vector2[centre.Count];
        var normal = new Vector2(0f, 1f);
        for (var i = 0; i < centre.Count; i++)
        {
            var tangent = centre[Math.Min(i + 1, centre.Count - 1)] - centre[Math.Max(i - 1, 0)];
            if (tangent.LengthSquared() > 1e-8f)
            {
                tangent = Vector2.Normalize(tangent);
                normal = new Vector2(-tangent.Y, tangent.X);
            }

            var half = radius * Math.Clamp(1f - (age[i] / Stroke.TrailWindowMs), 0f, 1f);
            left[i] = centre[i] + (normal * half);
            right[i] = centre[i] - (normal * half);
        }

        return [.. left, .. right.Reverse()];
    }
}
