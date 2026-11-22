using System.Numerics;
using Edgepad.Annotation;
using Xunit;

namespace Edgepad.Tests;

/// <summary>The laser's contract (K1): a 250 ms trail, a 400 ms watchdog, three sizes. A fake clock drives it.</summary>
public sealed class LaserTests
{
    private const byte Up = 0;
    private const byte Down = 1;
    private const byte Move = 2;

    private long now;

    private StrokeBoard Laser(StrokeTool tool = StrokeTool.LaserTrail, int width = 15)
    {
        var board = new StrokeBoard(() => now);
        board.Begin(BoardMode.Annotate);
        Assert.True(board.SetWidth(width));
        Assert.True(board.SetTool(tool));
        return board;
    }

    private static Stroke Trailing(params long[] atMs)
    {
        var stroke = new Stroke(StrokeTool.LaserTrail, PenStyle.Default);
        for (var i = 0; i < atMs.Length; i++)
        {
            stroke.Extend(new NormPoint((ushort)(i * 100), (ushort)(i * 100)), atMs[i]);
        }

        return stroke;
    }

    [Theory]
    [InlineData(249, 2)]
    [InlineData(250, 2)]
    [InlineData(251, 1)]
    public void ATrailPointOlderThan250MsIsGone(long age, int expectedPoints)
    {
        var stroke = Trailing(0, 100);
        stroke.TrimTrail(age);

        Assert.Equal(expectedPoints, stroke.Points.Count);
    }

    [Fact]
    public void ARestingFingersTrailIsEmptyAfter250Ms()
    {
        var board = Laser();
        Assert.True(board.Pen(0, 0, Down));
        now = 100;
        Assert.True(board.Pen(500, 500, Move));

        Assert.NotEmpty(board.Active!.Trail(1920, 1080, now).Points);
        Assert.Empty(board.Active.Trail(1920, 1080, now + 251).Points);
    }

    [Theory]
    [InlineData(0, 1f)]
    [InlineData(399, 1f)]
    [InlineData(400, 1f)]
    [InlineData(475, 0.5f)]
    [InlineData(550, 0f)]
    [InlineData(900, 0f)]
    public void TheDotFadesOverTheLast150MsOfTheWatchdog(long silentMs, float visible) =>
        Assert.Equal(visible, LaserGeometry.Visibility(1000 + silentMs, 1000), 3);

    [Fact]
    public void ALostPenUpLeavesNothingAfter550Ms()
    {
        var board = Laser(StrokeTool.Laser);
        Assert.True(board.Pen(100, 100, Down));
        Assert.True(board.LaserVisible(now + 399));
        Assert.True(board.LaserVisible(now + 549));
        Assert.False(board.LaserVisible(now + 550));
    }

    [Fact]
    public void APenEventRestartsTheWatchdog()
    {
        var board = Laser();
        Assert.True(board.Pen(0, 0, Down));
        now = 300;
        Assert.True(board.Pen(1, 1, Move));

        Assert.True(board.LaserVisible(now + 549));
        Assert.False(board.LaserVisible(now + 550));
    }

    [Fact]
    public void PenUpDropsTheStrokeAtOnce()
    {
        var board = Laser();
        Assert.True(board.Pen(0, 0, Down));
        Assert.True(board.Pen(1, 1, Up));

        Assert.Null(board.Active);
        Assert.False(board.LaserVisible(now));
        Assert.Empty(board.Strokes);
    }

    [Fact]
    public void PenAndShapeStrokesAreNeverLaserVisible()
    {
        var board = new StrokeBoard(() => now);
        board.Begin(BoardMode.Annotate);
        Assert.True(board.Pen(0, 0, Down));

        Assert.False(board.LaserVisible(now));
    }

    [Fact]
    public void ReadingWhilePenEventsArriveStaysConsistent()
    {
        // The Annotator takes one lock for both; this is the order a reader and a writer can interleave in.
        var board = Laser();
        Assert.True(board.Pen(0, 0, Down));
        for (ushort i = 1; i <= 50; i++)
        {
            now += 20;
            Assert.True(board.LaserVisible(now));
            Assert.True(board.Pen(i, i, Move));
            var (points, ages) = board.Active!.Trail(1920, 1080, now);
            Assert.Equal(points.Length, ages.Length);
            Assert.All(ages, a => Assert.InRange(a, 0f, 250f));
        }
    }

    [Theory]
    [InlineData(10, 5.4f)]
    [InlineData(15, 8.1f)]
    [InlineData(20, 10.8f)]
    public void TheDotRadiusIs005PercentOfScreenHeightPerStep(int width, float radius) =>
        Assert.Equal(radius, new Stroke(StrokeTool.Laser, PenStyle.Default with { Width = width }).LaserRadiusPx(1080), 2);

    [Fact]
    public void TheDotHasAMinimumOfTwoPixelsAndTheGlowIs18Times()
    {
        Assert.Equal(2f, new Stroke(StrokeTool.Laser, PenStyle.Default with { Width = 10 }).LaserRadiusPx(100), 3);
        Assert.Equal(1.8f, Stroke.LaserGlow, 3);
    }

    private static (Vector2[] Points, float[] Ages) Line(int n) =>
        ([.. Enumerable.Range(0, n).Select(i => new Vector2(i * 20f, 0f))],
         [.. Enumerable.Range(0, n).Select(i => 250f * (n - 1 - i) / (n - 1))]);

    [Fact]
    public void AFewerThanTwoPointsTrailHasNoOutline()
    {
        Assert.Empty(LaserGeometry.Outline([], [], 8f));
        Assert.Empty(LaserGeometry.Outline([new Vector2(1, 1)], [0f], 8f));
    }

    [Fact]
    public void TheOutlineIsOneClosedPolygonTaperingFromTheDotToNothing()
    {
        const float radius = 8f;
        var (points, ages) = Line(6);
        var outline = LaserGeometry.Outline(points, ages, radius);

        var perSide = ((points.Length - 1) * LaserGeometry.Subdivisions) + 1;
        Assert.Equal(2 * perSide, outline.Length);

        var left = outline[..perSide];
        var right = outline[perSide..].Reverse().ToArray();

        // Tail: both edges meet at the oldest point. Head: they are a full diameter apart.
        Assert.Equal(0f, Vector2.Distance(left[0], right[0]), 3);
        Assert.Equal(radius, Vector2.Distance(left[^1], points[^1]), 3);
        Assert.Equal(radius, Vector2.Distance(right[^1], points[^1]), 3);

        // Symmetric: each pair of edge points is the same distance from the centre line (y = 0 here).
        for (var i = 0; i < perSide; i++)
        {
            Assert.Equal(left[i].X, right[i].X, 3);
            Assert.Equal(left[i].Y, -right[i].Y, 3);
        }
    }

    [Fact]
    public void EveryOutlinePointStaysInsideTheCapsuleAroundTheTrail()
    {
        const float radius = 8f;
        var (points, ages) = Line(6);

        foreach (var v in LaserGeometry.Outline(points, ages, radius))
        {
            Assert.InRange(v.X, points[0].X - radius, points[^1].X + radius);
            Assert.InRange(v.Y, -radius, radius);
        }
    }

    [Fact]
    public void TrailAlphaRunsFromPoint9AtTheHeadToNothingAtTheTail()
    {
        Assert.Equal(0.9f, LaserGeometry.TrailAlpha(0), 3);
        Assert.Equal(0f, LaserGeometry.TrailAlpha(250), 3);
        Assert.InRange(LaserGeometry.TrailAlpha(125), 0.01f, 0.89f);
    }
}
