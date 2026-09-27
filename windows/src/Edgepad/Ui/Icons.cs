using System.Drawing.Drawing2D;
using System.Globalization;

namespace Edgepad.Ui;

/// <summary>
/// The design's line icons: Lucide's path data on a 24-unit grid, stroked rather than filled, exactly as the
/// phone and the design reference draw them. Kept as the SVG strings themselves rather than hand-translated
/// into GDI+ calls, so an icon can be checked against its source by eye and a new one pasted in whole.
/// </summary>
internal static class Icons
{
    public const string Smartphone = "M7 2h10a2 2 0 0 1 2 2v16a2 2 0 0 1 -2 2H7a2 2 0 0 1 -2 -2V4a2 2 0 0 1 2 -2zM12 18h.01";
    public const string Power = "M12 2v10M18.4 6.6a9 9 0 1 1 -12.77 .04";
    public const string Macro = "M4,3h5a1,1 0 0,1 1,1v5a1,1 0 0,1 -1,1h-5a1,1 0 0,1 -1,-1v-5a1,1 0 0,1 1,-1z"
        + "M15,3h5a1,1 0 0,1 1,1v5a1,1 0 0,1 -1,1h-5a1,1 0 0,1 -1,-1v-5a1,1 0 0,1 1,-1z"
        + "M15,14h5a1,1 0 0,1 1,1v5a1,1 0 0,1 -1,1h-5a1,1 0 0,1 -1,-1v-5a1,1 0 0,1 1,-1z"
        + "M4,14h5a1,1 0 0,1 1,1v5a1,1 0 0,1 -1,1h-5a1,1 0 0,1 -1,-1v-5a1,1 0 0,1 1,-1z";
    public const string FileText = "M15 2H6a2 2 0 0 0 -2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2 -2V7zM14 2v4a2 2 0 0 0 2 2h4M10 9H8M16 13H8M16 17H8";
    public const string BookOpen = "M12 7v14M3 18a1 1 0 0 1 -1 -1V4a1 1 0 0 1 1 -1h5a4 4 0 0 1 4 4 4 4 0 0 1 4 -4h5a1 1 0 0 1 1 1v13"
        + "a1 1 0 0 1 -1 1h-6a3 3 0 0 0 -3 3 3 3 0 0 0 -3 -3z";
    public const string Unlink = "m18.84 12.25 1.72 -1.71h-.02a5.004 5.004 0 0 0 -.12 -7.07 5.006 5.006 0 0 0 -6.95 0l-1.72 1.71"
        + "M5.17 11.75l-1.71 1.71a5.004 5.004 0 0 0 .12 7.07 5.006 5.006 0 0 0 6.95 0l1.71 -1.71M8 2v3M2 8h3M16 19v3M19 16h3";
    public const string Close = "M18 6L6 18M6 6l12 12";
    public const string Volume = "M11 4.702a.705 .705 0 0 0 -1.203 -.498L6.413 7.587A1.4 1.4 0 0 1 5.416 8H3a1 1 0 0 0 -1 1v6a1 1 0 0 0 1 1"
        + "h2.416a1.4 1.4 0 0 1 .997 .413l3.383 3.384A.705 .705 0 0 0 11 19.298zM16 9a5 5 0 0 1 0 6M19.364 18.364a9 9 0 0 0 0 -12.728";
    public const string VolumeMuted = "M11 4.702a.705 .705 0 0 0 -1.203 -.498L6.413 7.587A1.4 1.4 0 0 1 5.416 8H3a1 1 0 0 0 -1 1v6"
        + "a1 1 0 0 0 1 1h2.416a1.4 1.4 0 0 1 .997 .413l3.383 3.384A.705 .705 0 0 0 11 19.298zM22 9l-6 6M16 9l6 6";
    public const string Sun = "M12 8a4 4 0 1 0 0 8 4 4 0 1 0 0 -8zM12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2"
        + "M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41";
    public const string Microphone = "M12 2a3 3 0 0 0 -3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0 -3 -3zM19 10v2a7 7 0 0 1 -14 0v-2M12 19v3";
    public const string Grip = "M9 5h.01M9 12h.01M9 19h.01M15 5h.01M15 12h.01M15 19h.01";
    public const string Plus = "M5 12h14M12 5v14";
    public const string FolderOpen = "M6 14l1.5 -2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1 -1.95 1.5H4a2 2 0 0 1 -2 -2V5"
        + "a2 2 0 0 1 2 -2h3.9a2 2 0 0 1 1.69 .9l.81 1.2a2 2 0 0 0 1.67 .9H18a2 2 0 0 1 2 2v2";
    public const string Trash = "M3 6h18M19 6v14c0 1 -1 2 -2 2H7c-1 0 -2 -1 -2 -2V6M8 6V4c0 -1 1 -2 2 -2h4c1 0 2 1 2 2v2M10 11v6M14 11v6";

    /// <summary>The grid every path above is drawn on.</summary>
    private const float Grid = 24f;

    /// <summary>Parsed once each: the tray repaints its header thirty times a second while the ripple runs.</summary>
    private static readonly Dictionary<string, GraphicsPath> Parsed = [];

    /// <summary>
    /// Strokes <paramref name="icon"/> into <paramref name="box"/>. The stroke is given in grid units, as Lucide
    /// gives it, so a 16 px icon gets a line as much thinner than a 24 px one as the design's does.
    /// </summary>
    public static void Draw(Graphics graphics, string icon, RectangleF box, Color colour, float stroke = 2f)
    {
        if (!Parsed.TryGetValue(icon, out var path))
        {
            path = Parse(icon);
            Parsed[icon] = path;
        }

        var scale = Math.Min(box.Width, box.Height) / Grid;
        using var placed = (GraphicsPath)path.Clone();
        using (var matrix = new Matrix(scale, 0, 0, scale, box.X, box.Y))
        {
            placed.Transform(matrix);
        }

        using var pen = new Pen(colour, stroke * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        var smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.DrawPath(pen, placed);
        graphics.SmoothingMode = smoothing;
    }

    /// <summary>
    /// SVG path data as a GraphicsPath: the move, line, horizontal, vertical, cubic, arc and close commands in
    /// both their absolute and relative forms, which is every command Lucide's icons use.
    /// </summary>
    internal static GraphicsPath Parse(string data)
    {
        var path = new GraphicsPath();
        var reader = new PathReader(data);
        var current = PointF.Empty;
        var start = PointF.Empty;
        var command = 'M';

        while (reader.More())
        {
            if (reader.Command() is { } next)
            {
                command = next;
            }

            var relative = char.IsLower(command);
            var origin = relative ? current : PointF.Empty;
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    path.StartFigure();
                    current = start = Offset(origin, reader.Number(), reader.Number());

                    // Pairs after a move are lines, in the same absolute or relative sense.
                    command = relative ? 'l' : 'L';
                    break;
                case 'L':
                    current = Line(path, current, Offset(origin, reader.Number(), reader.Number()));
                    break;
                case 'H':
                    current = Line(path, current, new PointF(reader.Number() + origin.X, current.Y));
                    break;
                case 'V':
                    current = Line(path, current, new PointF(current.X, reader.Number() + origin.Y));
                    break;
                case 'C':
                    var first = Offset(origin, reader.Number(), reader.Number());
                    var second = Offset(origin, reader.Number(), reader.Number());
                    var end = Offset(origin, reader.Number(), reader.Number());
                    path.AddBezier(current, first, second, end);
                    current = end;
                    break;
                case 'A':
                    var radius = reader.Number();
                    _ = reader.Number(); // The second radius: every Lucide arc is circular. See Arc.
                    _ = reader.Number(); // The rotation, which a circle does not have.
                    var large = reader.Number() != 0;
                    var clockwise = reader.Number() != 0;
                    var to = Offset(origin, reader.Number(), reader.Number());
                    Arc(path, current, to, radius, large, clockwise);
                    current = to;
                    break;
                case 'Z':
                    path.CloseFigure();
                    current = start;
                    break;
                default:
                    throw new FormatException($"Unsupported path command '{command}' in an icon");
            }
        }

        return path;
    }

    private static PointF Offset(PointF origin, float x, float y) => new(origin.X + x, origin.Y + y);

    private static PointF Line(GraphicsPath path, PointF from, PointF to)
    {
        path.AddLine(from, to);
        return to;
    }

    /// <summary>
    /// An SVG arc as GDI+ draws one: SVG names the two ends and lets the flags pick the circle, GDI+ wants the
    /// circle and two angles. This is the spec's endpoint-to-centre conversion (SVG 1.1, appendix F.6.5) with
    /// the rotation and the second radius taken out.
    /// </summary>
    // ponytail: circles only, which is every arc Lucide draws. An elliptical or rotated arc would need the full
    // conversion and a Bézier approximation, since GDI+'s AddArc measures its angles differently on an ellipse.
    private static void Arc(GraphicsPath path, PointF from, PointF to, float radius, bool large, bool clockwise)
    {
        if (from == to)
        {
            return;
        }

        var r = (double)Math.Abs(radius);
        var halfX = (from.X - to.X) / 2.0;
        var halfY = (from.Y - to.Y) / 2.0;
        var squared = (halfX * halfX) + (halfY * halfY);
        if (r == 0)
        {
            path.AddLine(from, to);
            return;
        }

        // A radius too small to span the two ends is scaled up until it just does, as the spec requires.
        r = Math.Max(r, Math.Sqrt(squared));

        var reach = Math.Sqrt(Math.Max(0, ((r * r) - squared) / squared)) * (large == clockwise ? -1 : 1);
        var centreX = (reach * halfY) + ((from.X + to.X) / 2.0);
        var centreY = (-reach * halfX) + ((from.Y + to.Y) / 2.0);

        var begin = Math.Atan2(from.Y - centreY, from.X - centreX);
        var sweep = Math.Atan2(to.Y - centreY, to.X - centreX) - begin;
        if (clockwise && sweep < 0)
        {
            sweep += 2 * Math.PI;
        }
        else if (!clockwise && sweep > 0)
        {
            sweep -= 2 * Math.PI;
        }

        // GDI+ and SVG agree that angles grow clockwise on a y-down screen, so the angles carry over as they are.
        path.AddArc(
            (float)(centreX - r),
            (float)(centreY - r),
            (float)(r * 2),
            (float)(r * 2),
            (float)(begin * 180 / Math.PI),
            (float)(sweep * 180 / Math.PI));
    }

    /// <summary>Reads SVG path data a token at a time, where "1-14" is two numbers and ".7.7" is too.</summary>
    private sealed class PathReader(string data)
    {
        private int at;

        public bool More()
        {
            Skip();
            return at < data.Length;
        }

        /// <summary>The command letter here, or null when the data carries on with the last one's numbers.</summary>
        public char? Command()
        {
            Skip();
            return at < data.Length && char.IsLetter(data[at]) ? data[at++] : null;
        }

        public float Number()
        {
            Skip();
            var begin = at;
            if (at < data.Length && data[at] is '-' or '+')
            {
                at++;
            }

            var dot = false;
            while (at < data.Length && (char.IsDigit(data[at]) || (data[at] == '.' && !dot)))
            {
                dot |= data[at] == '.';
                at++;
            }

            if (at == begin)
            {
                throw new FormatException($"Expected a number at {begin} in an icon");
            }

            return float.Parse(data.AsSpan(begin, at - begin), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private void Skip()
        {
            while (at < data.Length && (char.IsWhiteSpace(data[at]) || data[at] == ','))
            {
                at++;
            }
        }
    }
}
