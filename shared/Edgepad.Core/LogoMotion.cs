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

    /// <summary>The tile's outline, exactly as the logo SVG's `bg` path (continuous corners): the one copy of its numbers.</summary>
    public const string TilePath =
        "M354.65 0H669.35C771.47 0 822.53 0 868.58 15.2C937.64 37.99 986.01 86.36 1008.8 155.42C1024 201.47 1024 252.53 1024 354.65V669.35C1024 771.47 1024 822.53 1008.8 868.58C986.01 937.64 937.64 986.01 868.58 1008.8C822.53 1024 771.47 1024 669.35 1024H354.65C252.53 1024 201.47 1024 155.42 1008.8C86.36 986.01 37.99 937.64 15.2 868.58C0 822.53 0 771.47 0 669.35V354.65C0 252.53 0 201.47 15.2 155.42C37.99 86.36 86.36 37.99 155.42 15.2C201.47 0 252.53 0 354.65 0Z";

    /// <summary>The tile as a start point and segments: two numbers is a line to there, six a cubic (two controls, then the end). Closed.</summary>
    public static readonly (double X, double Y) TileStart = (354.65, 0);

    public static readonly IReadOnlyList<double[]> TileSegments = ParseTile();

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

    private static List<double[]> ParseTile()
    {
        var tokens = new List<string>();
        var number = new System.Text.StringBuilder();

        void Flush()
        {
            if (number.Length > 0)
            {
                tokens.Add(number.ToString());
                number.Clear();
            }
        }

        foreach (var ch in TilePath)
        {
            if (char.IsLetter(ch))
            {
                Flush();
                tokens.Add(ch.ToString());
            }
            else if (ch == ' ')
            {
                Flush();
            }
            else
            {
                number.Append(ch);
            }
        }

        Flush();
        var segments = new List<double[]>();
        double x = 0;
        double y = 0;
        var at = 0;
        var command = ' ';

        double Next() => double.Parse(tokens[at++], System.Globalization.CultureInfo.InvariantCulture);

        while (at < tokens.Count)
        {
            if (char.IsLetter(tokens[at][0]))
            {
                command = tokens[at++][0];
            }

            switch (command)
            {
                case 'M':
                    x = Next();
                    y = Next();
                    break;
                case 'H':
                    x = Next();
                    segments.Add([x, y]);
                    break;
                case 'V':
                    y = Next();
                    segments.Add([x, y]);
                    break;
                case 'C':
                    double[] curve = [Next(), Next(), Next(), Next(), Next(), Next()];
                    x = curve[4];
                    y = curve[5];
                    segments.Add(curve);
                    break;
                default:
                    return segments;
            }
        }

        return segments;
    }

    private static double Spring(double x) => x >= 1 ? 1 : 1 - (Math.Exp(-6 * x) * Math.Cos(9 * x));
}

/// <summary>One part of the logo: the arc (<paramref name="Arc"/>, radius <paramref name="Outer"/>) or a tick from <paramref name="Inner"/> to <paramref name="Outer"/> at <paramref name="Angle"/>.</summary>
internal readonly record struct LogoStroke(bool Arc, double Angle, double Inner, double Outer, bool Accent);

/// <summary>How far a part has sprung out: its radii are multiplied by <paramref name="Scale"/>, it is drawn at <paramref name="Opacity"/>.</summary>
internal readonly record struct LogoPose(double Scale, double Opacity);
