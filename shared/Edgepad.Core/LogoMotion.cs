namespace Edgepad;

/// <summary>
/// The logo's 0.75 s pop, as numbers: scripts/logo-motion.ps1 is the source, this is its twin for the laptops'
/// windows. Geometry is on the 1024-unit logo canvas; angles run clockwise from the right (0 = right, 90 = down).
/// Pure, so the maths is a test and every platform draws from the same frame.
/// </summary>
internal static class LogoMotion
{
    public const double Canvas = 1024;
    public const double Pivot = 881.8;
    public const double StrokeWidth = 88;
    public const double ArcRadius = 288;
    public const double TotalMs = 760;

    private const double Stagger = 60;
    private const double PartMs = 420;
    private const double FromScale = 0.55;
    private const double FadeIn = 0.25;

    /// <summary>The arc, then each tick in angle order (the needle is the middle one), as the pop plays them.</summary>
    public static readonly IReadOnlyList<LogoStroke> Strokes =
    [
        new(true, 180, 0, ArcRadius, true),
        new(false, 180, 424, 656, false),
        new(false, 202.5, 424, 656, false),
        new(false, 225, 424, 720, true),
        new(false, 247.5, 424, 656, false),
        new(false, 270, 424, 656, false),
    ];

    /// <summary>Each stroke's radius scale (toward the pivot) and opacity at <paramref name="ms"/> into the pop.</summary>
    public static LogoPose[] Frame(double ms)
    {
        var poses = new LogoPose[Strokes.Count];
        for (var i = 0; i < poses.Length; i++)
        {
            var x = Math.Clamp((ms - (i * Stagger)) / PartMs, 0, 1);
            poses[i] = new LogoPose(FromScale + ((1 - FromScale) * Spring(x)), Math.Clamp(x / FadeIn, 0, 1));
        }

        return poses;
    }

    private static double Spring(double x) => x >= 1 ? 1 : 1 - (Math.Exp(-6 * x) * Math.Cos(9 * x));
}

/// <summary>One part of the logo: the arc (<paramref name="Arc"/>, radius <paramref name="Outer"/>) or a tick from <paramref name="Inner"/> to <paramref name="Outer"/> at <paramref name="Angle"/>.</summary>
internal readonly record struct LogoStroke(bool Arc, double Angle, double Inner, double Outer, bool Accent);

/// <summary>How far a part has sprung out: its radii are multiplied by <paramref name="Scale"/>, it is drawn at <paramref name="Opacity"/>.</summary>
internal readonly record struct LogoPose(double Scale, double Opacity);
