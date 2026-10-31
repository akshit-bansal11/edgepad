using System.Numerics;
using Edgepad.Protocol;

namespace Edgepad.Annotation;

/// <summary>Whether strokes are being taken at all, and from where they may come.</summary>
internal enum BoardMode
{
    Off,

    /// <summary>The overlay is up and every tool draws on it.</summary>
    Annotate,

    /// <summary>Nothing is shown on this laptop and only the pen tool is accepted: a signature is a pen line.</summary>
    Sign,
}

internal enum StrokeTool
{
    Pen,
    Highlighter,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
}

/// <summary>A point as the phone sent it: 0 is the left/top edge of the laptop screen, 65535 the right/bottom.</summary>
internal readonly record struct NormPoint(ushort X, ushort Y);

/// <summary>
/// What the next stroke will look like, fixed when its finger goes down so a later change never repaints ink already
/// on the screen. Width and opacity are the phone's own numbers (SET PEN_WIDTH and PEN_OPACITY), kept as sent and
/// turned into pixels and alpha only where a stroke is drawn.
/// </summary>
/// <param name="Rgb">0xRRGGBB, from the palette or from <see cref="TryParseRgb"/>; nothing else can reach it.</param>
/// <param name="Width">1..100, in steps of 0.02% of the screen's height.</param>
/// <param name="Opacity">10..100 percent.</param>
internal readonly record struct PenStyle(int Rgb, int Width, int Opacity)
{
    /// <summary>
    /// 20 steps are 0.4% of the screen's height, the pen's width before the phone could choose, and a round number
    /// to send as the phone's default.
    /// </summary>
    public const int DefaultWidth = 20;

    /// <summary>Below this the width is the 0.1% floor: 1..4 are accepted and all mean 5, so a slider may start at 5.</summary>
    public const int MinWidth = 5;

    public const int MaxWidth = 100;

    public const int DefaultOpacity = 100;
    public const int MinOpacity = 10;
    public const int MaxOpacity = 100;

    /// <summary>The highlighter is this many times as wide as the pen is set to be, which is today's 2% against 0.4%.</summary>
    public const float HighlighterWidths = 5f;

    /// <summary>One width step is 0.02% of the screen's height, written as a divisor so 20 steps are exactly 0.004.</summary>
    private const float WidthStep = 5000f;

    /// <summary>Black, the first palette colour, at the default width and fully opaque.</summary>
    public static PenStyle Default { get; } = new(0x000000, DefaultWidth, DefaultOpacity);

    public static bool IsWidth(int value) => value is >= 1 and <= MaxWidth;

    public static bool IsOpacity(int value) => value is >= MinOpacity and <= MaxOpacity;

    /// <summary>The pen's width as a share of the screen's height, 0.001 to 0.02.</summary>
    public float PenShare => Math.Max(MinWidth, Width) / WidthStep;

    /// <summary>
    /// Exactly six hex digits, either case, and nothing around them: no "#", no sign, no space, no "0x". Parsed by
    /// hand because the number parsers in the framework accept more than that, and this one string is the phone's
    /// to choose.
    /// </summary>
    public static bool TryParseRgb(string? text, out int rgb)
    {
        rgb = 0;
        if (text is not { Length: 6 } || !text.All(char.IsAsciiHexDigit))
        {
            return false;
        }

        rgb = Convert.ToInt32(text, 16);
        return true;
    }
}

/// <summary>
/// One finger-down to finger-up. Freehand tools keep every point; the shape tools keep the down point and the
/// latest one, because a shape is only ever its two corners.
/// </summary>
internal sealed class Stroke(StrokeTool tool, PenStyle style)
{
    /// <summary>
    /// The highlighter is translucent so the text under it stays readable: 35% of its colour, times the opacity the
    /// phone set. It is stroked once per path, so an overlapping stretch of one stroke is not darker than the rest.
    /// </summary>
    public const float HighlighterAlpha = 0.35f;

    /// <summary>The arrow head's wings are this many pen widths long.</summary>
    private const float HeadWidths = 4f;

    /// <summary>Radians between the shaft and a wing, a little under 30 degrees.</summary>
    private const float HeadAngle = 0.5f;

    public StrokeTool Tool { get; } = tool;

    public PenStyle Style { get; } = style;

    /// <summary>The colour as 0xRRGGBB, without the alpha <see cref="Alpha"/> gives it.</summary>
    public int Rgb => Style.Rgb;

    /// <summary>The ink's alpha, 0..255: the opacity the phone set, and for the highlighter that of its 35%.</summary>
    public byte Alpha =>
        (byte)Math.Round(255f * (Tool == StrokeTool.Highlighter ? HighlighterAlpha : 1f) * Style.Opacity / 100f);

    public List<NormPoint> Points { get; } = [];

    private bool IsShape => Tool is StrokeTool.Line or StrokeTool.Arrow or StrokeTool.Rectangle or StrokeTool.Ellipse;

    /// <summary>The width as a share of the screen's height, so a stroke looks alike on any display; at least one pixel.</summary>
    public float WidthPx(int height) =>
        Math.Max(1f, Style.PenShare * (Tool == StrokeTool.Highlighter ? PenStyle.HighlighterWidths : 1f) * height);

    /// <summary>The points in pixels of a <paramref name="width"/> by <paramref name="height"/> screen.</summary>
    public Vector2[] Path(int width, int height) =>
        [.. Points.Select(p => new Vector2(p.X / 65535f * (width - 1), p.Y / 65535f * (height - 1)))];

    /// <summary>
    /// The outer ends of an arrow head, which meet the tip at the shaft's end. Empty for anything but an arrow,
    /// and for an arrow with no length yet, which has no direction to point in.
    /// </summary>
    public Vector2[] ArrowWings(int width, int height)
    {
        var path = Path(width, height);
        if (Tool != StrokeTool.Arrow || path.Length < 2 || path[1] == path[0])
        {
            return [];
        }

        var back = Vector2.Normalize(path[0] - path[1]) * (HeadWidths * WidthPx(height));
        return [path[1] + Rotate(back, HeadAngle), path[1] + Rotate(back, -HeadAngle)];
    }

    /// <summary>The box the ink fills, half the stroke's width beyond every point; null before any point.</summary>
    public (Vector2 Min, Vector2 Max)? Ink(int width, int height)
    {
        var points = Path(width, height).Concat(ArrowWings(width, height)).ToArray();
        if (points.Length == 0)
        {
            return null;
        }

        var half = new Vector2(WidthPx(height) / 2f);
        return (points.Aggregate(Vector2.Min) - half, points.Aggregate(Vector2.Max) + half);
    }

    /// <summary>A shape keeps two points: the down point, then wherever the finger is now.</summary>
    internal void Extend(NormPoint point)
    {
        if (IsShape && Points.Count > 1)
        {
            Points[1] = point;
        }
        else
        {
            Points.Add(point);
        }
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        var (sin, cos) = MathF.SinCos(radians);
        return new Vector2((v.X * cos) - (v.Y * sin), (v.X * sin) + (v.Y * cos));
    }
}

/// <summary>
/// The strokes of one annotate or sign session, and the rules for which PEN frame may change them. Pure: no
/// window and no drawing, so the overlay and the signature export both read it and a test can drive it.
/// </summary>
internal sealed class StrokeBoard
{
    /// <summary>
    /// A held finger sends a point every few milliseconds and nothing else bounds a stroke, so one is capped
    /// rather than left to grow until the process does.
    /// </summary>
    internal const int MaxPointsPerStroke = 20_000;

    /// <summary>
    /// The whole board's ceiling. The per-stroke cap alone bounds nothing: every PEN down starts a new stroke,
    /// so a stream of them would grow the board, and every redraw, without end. Past it new ink is dropped
    /// until Undo or Clear makes room, rather than old ink being thrown away unasked.
    /// </summary>
    internal const int MaxPointsPerBoard = 200_000;

    private readonly List<Stroke> strokes = [];

    // The points held in strokes, so the board ceiling is checked without walking every stroke per frame.
    private int committedPoints;

    public BoardMode Mode { get; private set; }

    public StrokeTool Tool { get; private set; } = StrokeTool.Pen;

    /// <summary>The colour, width and opacity the next stroke takes. They outlive <see cref="Begin"/>, as the colour always did, and end with the board.</summary>
    public PenStyle Style { get; private set; } = PenStyle.Default;

    /// <summary>The strokes whose finger has lifted, oldest first.</summary>
    public IReadOnlyList<Stroke> Strokes => strokes;

    /// <summary>The stroke under the finger now: drawn by the overlay, but not yet undoable or exportable.</summary>
    public Stroke? Active { get; private set; }

    /// <summary>Starts a session from nothing, so the last session's strokes never leak into this one.</summary>
    public void Begin(BoardMode mode)
    {
        strokes.Clear();
        committedPoints = 0;
        Active = null;
        Mode = mode;
        Tool = StrokeTool.Pen;
    }

    /// <summary>Ends the session and throws its strokes away.</summary>
    public void End() => Begin(BoardMode.Off);

    /// <summary>False when the tool is not allowed in this mode, so the caller counts the frame as dropped.</summary>
    public bool SetTool(StrokeTool tool)
    {
        if (Mode == BoardMode.Off || (Mode == BoardMode.Sign && tool != StrokeTool.Pen))
        {
            return false;
        }

        Tool = tool;
        return true;
    }

    /// <summary>False when <paramref name="index"/> is not in the palette.</summary>
    public bool SetColour(int index)
    {
        if (Palette.Colour(index) is not { } rgb)
        {
            return false;
        }

        Style = Style with { Rgb = rgb };
        return true;
    }

    /// <summary>Any 0xRRGGBB: the caller has already checked it came from <see cref="PenStyle.TryParseRgb"/>.</summary>
    public bool SetRgb(int rgb)
    {
        if (rgb is < 0 or > 0xFFFFFF)
        {
            return false;
        }

        Style = Style with { Rgb = rgb };
        return true;
    }

    /// <summary>False, and nothing changes, unless <paramref name="width"/> is 1..100.</summary>
    public bool SetWidth(int width)
    {
        if (!PenStyle.IsWidth(width))
        {
            return false;
        }

        Style = Style with { Width = width };
        return true;
    }

    /// <summary>False, and nothing changes, unless <paramref name="opacity"/> is 10..100.</summary>
    public bool SetOpacity(int opacity)
    {
        if (!PenStyle.IsOpacity(opacity))
        {
            return false;
        }

        Style = Style with { Opacity = opacity };
        return true;
    }

    /// <summary>Takes back the last finished stroke. False when there is none or no session.</summary>
    public bool Undo()
    {
        if (Mode == BoardMode.Off || strokes.Count == 0)
        {
            return false;
        }

        committedPoints -= strokes[^1].Points.Count;
        strokes.RemoveAt(strokes.Count - 1);
        return true;
    }

    /// <summary>Empties the board, a stroke in progress included. False outside a session.</summary>
    public bool Clear()
    {
        if (Mode == BoardMode.Off)
        {
            return false;
        }

        strokes.Clear();
        committedPoints = 0;
        Active = null;
        return true;
    }

    /// <summary>
    /// One PEN frame. False means dropped: no session, a state that is not up/down/move (a newer phone may add
    /// states), or a move or up with no finger down. An up commits the stroke, and a down while one is still
    /// open commits that one first, because the up that would have ended it was lost.
    /// </summary>
    public bool Pen(ushort x, ushort y, byte state)
    {
        if (Mode == BoardMode.Off)
        {
            return false;
        }

        var point = new NormPoint(x, y);
        switch (state)
        {
            case 1:
                Commit();
                if (committedPoints >= MaxPointsPerBoard)
                {
                    return false;
                }

                Active = new Stroke(Tool, Style);
                Active.Points.Add(point);
                return true;
            case 2 when Active is not null:
                if (Active.Points.Count >= MaxPointsPerStroke
                    || committedPoints + Active.Points.Count >= MaxPointsPerBoard)
                {
                    return false;
                }

                Active.Extend(point);
                return true;
            case 0 when Active is not null:
                Active.Extend(point);
                Commit();
                return true;
            default:
                return false;
        }
    }

    private void Commit()
    {
        if (Active is not null)
        {
            strokes.Add(Active);
            committedPoints += Active.Points.Count;
            Active = null;
        }
    }
}
