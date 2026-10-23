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
/// One finger-down to finger-up. Freehand tools keep every point; the shape tools keep the down point and the
/// latest one, because a shape is only ever its two corners.
/// </summary>
internal sealed class Stroke(StrokeTool tool, int colour)
{
    /// <summary>Pen and highlighter widths, as a share of the screen's height so a stroke looks alike on any display.</summary>
    public const float PenShare = 0.004f;
    public const float HighlighterShare = 0.02f;

    /// <summary>
    /// The highlighter is translucent so the text under it stays readable. It is stroked once per path, so an
    /// overlapping stretch of one stroke is not darker than the rest.
    /// </summary>
    public const float HighlighterAlpha = 0.35f;

    /// <summary>The arrow head's wings are this many pen widths long.</summary>
    private const float HeadWidths = 4f;

    /// <summary>Radians between the shaft and a wing, a little under 30 degrees.</summary>
    private const float HeadAngle = 0.5f;

    public StrokeTool Tool { get; } = tool;

    /// <summary>An index into <see cref="Palette"/>.</summary>
    public int Colour { get; } = colour;

    public List<NormPoint> Points { get; } = [];

    private bool IsShape => Tool is StrokeTool.Line or StrokeTool.Arrow or StrokeTool.Rectangle or StrokeTool.Ellipse;

    public float WidthPx(int height) =>
        Math.Max(1f, (Tool == StrokeTool.Highlighter ? HighlighterShare : PenShare) * height);

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

    private readonly List<Stroke> strokes = [];

    public BoardMode Mode { get; private set; }

    public StrokeTool Tool { get; private set; } = StrokeTool.Pen;

    public int Colour { get; private set; }

    /// <summary>The strokes whose finger has lifted, oldest first.</summary>
    public IReadOnlyList<Stroke> Strokes => strokes;

    /// <summary>The stroke under the finger now: drawn by the overlay, but not yet undoable or exportable.</summary>
    public Stroke? Active { get; private set; }

    /// <summary>Starts a session from nothing, so the last session's strokes never leak into this one.</summary>
    public void Begin(BoardMode mode)
    {
        strokes.Clear();
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
        if (Palette.Colour(index) is null)
        {
            return false;
        }

        Colour = index;
        return true;
    }

    /// <summary>Takes back the last finished stroke. False when there is none or no session.</summary>
    public bool Undo()
    {
        if (Mode == BoardMode.Off || strokes.Count == 0)
        {
            return false;
        }

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
                Active = new Stroke(Tool, Colour);
                Active.Points.Add(point);
                return true;
            case 2 when Active is not null:
                if (Active.Points.Count >= MaxPointsPerStroke)
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
            Active = null;
        }
    }
}
