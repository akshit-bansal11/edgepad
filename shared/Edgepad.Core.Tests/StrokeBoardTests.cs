using System.Numerics;
using Edgepad.Annotation;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The stroke model on its own: which PEN frames and tool changes a session accepts, and the geometry the
/// overlay and the export both draw from. No window, no bitmap.
/// </summary>
public sealed class StrokeBoardTests
{
    private const byte Up = 0;
    private const byte Down = 1;
    private const byte Move = 2;

    private static StrokeBoard Started(BoardMode mode = BoardMode.Annotate)
    {
        var board = new StrokeBoard();
        board.Begin(mode);
        return board;
    }

    private static void Draw(StrokeBoard board, ushort fromX, ushort fromY, ushort toX, ushort toY)
    {
        Assert.True(board.Pen(fromX, fromY, Down));
        Assert.True(board.Pen(toX, toY, Move));
        Assert.True(board.Pen(toX, toY, Up));
    }

    [Fact]
    public void ANewBoardIsOffAndAcceptsNothing()
    {
        var board = new StrokeBoard();

        Assert.Equal(BoardMode.Off, board.Mode);
        Assert.False(board.Pen(1, 1, Down));
        Assert.False(board.SetTool(StrokeTool.Pen));
        Assert.False(board.Undo());
        Assert.False(board.Clear());
        Assert.Empty(board.Strokes);
        Assert.Null(board.Active);
    }

    [Fact]
    public void ADownMoveUpIsOneStrokeAndIsFinishedOnlyOnTheUp()
    {
        var board = Started();

        Assert.True(board.Pen(10, 20, Down));
        Assert.True(board.Pen(30, 40, Move));
        Assert.Empty(board.Strokes);
        Assert.NotNull(board.Active);

        Assert.True(board.Pen(50, 60, Up));

        Assert.Null(board.Active);
        var stroke = Assert.Single(board.Strokes);
        Assert.Equal(new[] { new NormPoint(10, 20), new NormPoint(30, 40), new NormPoint(50, 60) }, stroke.Points);
    }

    [Theory]
    [InlineData(Move)]
    [InlineData(Up)]
    public void AMoveOrUpWithNoFingerDownIsDropped(byte state)
    {
        var board = Started();

        Assert.False(board.Pen(5, 5, state));
        Assert.Empty(board.Strokes);
        Assert.Null(board.Active);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(255)]
    public void AStateThatIsNotUpDownOrMoveIsDroppedEvenMidStroke(byte state)
    {
        var board = Started();
        Assert.True(board.Pen(1, 1, Down));

        Assert.False(board.Pen(2, 2, state));

        Assert.Single(board.Active!.Points);
    }

    [Fact]
    public void ADownWhileOneIsOpenFinishesTheOpenOneFirst()
    {
        var board = Started();
        Assert.True(board.Pen(1, 1, Down));
        Assert.True(board.Pen(2, 2, Move));

        Assert.True(board.Pen(9, 9, Down));

        Assert.Equal(2, Assert.Single(board.Strokes).Points.Count);
        Assert.Equal(new NormPoint(9, 9), Assert.Single(board.Active!.Points));
    }

    [Fact]
    public void AStrokeIsCappedRatherThanGrowingWithoutEnd()
    {
        var board = Started();
        Assert.True(board.Pen(0, 0, Down));
        for (var i = 1; i < StrokeBoard.MaxPointsPerStroke; i++)
        {
            Assert.True(board.Pen(1, 1, Move));
        }

        Assert.False(board.Pen(2, 2, Move));
        Assert.Equal(StrokeBoard.MaxPointsPerStroke, board.Active!.Points.Count);
    }

    [Fact]
    public void TheWholeBoardIsCappedAndUndoMakesRoom()
    {
        var board = Started();
        var strokes = 0;
        while (board.Pen(0, 0, Down))
        {
            var points = 1;
            while (points < StrokeBoard.MaxPointsPerStroke && board.Pen(1, 1, Move))
            {
                points++;
            }

            Assert.True(board.Pen(2, 2, Up));
            strokes++;
            Assert.True(strokes <= (StrokeBoard.MaxPointsPerBoard / StrokeBoard.MaxPointsPerStroke) + 1);
        }

        Assert.True(board.Undo());
        Assert.True(board.Pen(0, 0, Down));
    }

    [Fact]
    public void AToolChangeMidStrokeAppliesToTheNextStroke()
    {
        var board = Started();
        Assert.True(board.Pen(1, 1, Down));

        Assert.True(board.SetTool(StrokeTool.Rectangle));
        Assert.True(board.SetColour(2));
        Assert.True(board.Pen(2, 2, Up));
        Assert.True(board.Pen(3, 3, Down));

        Assert.Equal(StrokeTool.Pen, board.Strokes[0].Tool);
        Assert.Equal(0, board.Strokes[0].Colour);
        Assert.Equal(StrokeTool.Rectangle, board.Active!.Tool);
        Assert.Equal(2, board.Active.Colour);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void SigningAcceptsOnlyThePenTool(int toolId)
    {
        var tool = (StrokeTool)toolId;
        var board = Started(BoardMode.Sign);

        Assert.False(board.SetTool(tool));
        Assert.True(board.SetTool(StrokeTool.Pen));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AnnotatingAcceptsEveryTool(int toolId)
    {
        var tool = (StrokeTool)toolId;
        var board = Started();

        Assert.True(board.SetTool(tool));
        Assert.Equal(tool, board.Tool);
    }

    [Fact]
    public void ABeginStartsOnThePenWhateverTheLastSessionLeftSelected()
    {
        var board = Started();
        Assert.True(board.SetTool(StrokeTool.Ellipse));

        board.Begin(BoardMode.Sign);

        Assert.Equal(StrokeTool.Pen, board.Tool);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(-1, false)]
    public void OnlyAPaletteIndexSetsTheColour(int index, bool accepted)
    {
        var board = Started();

        Assert.Equal(accepted, board.SetColour(index));
        Assert.Equal(accepted ? index : 0, board.Colour);
    }

    [Fact]
    public void UndoTakesBackTheLastFinishedStrokeAndNeverTheOneUnderTheFinger()
    {
        var board = Started();
        Draw(board, 1, 1, 2, 2);
        Draw(board, 3, 3, 4, 4);
        Assert.True(board.Pen(5, 5, Down));

        Assert.True(board.Undo());

        Assert.Equal(new NormPoint(1, 1), Assert.Single(board.Strokes).Points[0]);
        Assert.NotNull(board.Active);
        Assert.True(board.Undo());
        Assert.False(board.Undo());
    }

    [Fact]
    public void ClearEmptiesTheBoardAndDiscardsAStrokeInProgress()
    {
        var board = Started();
        Draw(board, 1, 1, 2, 2);
        Assert.True(board.Pen(5, 5, Down));

        Assert.True(board.Clear());

        Assert.Empty(board.Strokes);
        Assert.Null(board.Active);
        Assert.Equal(BoardMode.Annotate, board.Mode);
        Assert.False(board.Pen(6, 6, Move));
    }

    [Fact]
    public void EndAndABeginBothDiscardTheStrokes()
    {
        var board = Started();
        Draw(board, 1, 1, 2, 2);

        board.End();
        Assert.Equal(BoardMode.Off, board.Mode);
        Assert.Empty(board.Strokes);
        Assert.False(board.Pen(1, 1, Down));

        board.Begin(BoardMode.Annotate);
        Draw(board, 1, 1, 2, 2);
        board.Begin(BoardMode.Sign);
        Assert.Empty(board.Strokes);
        Assert.Null(board.Active);
    }

    [Fact]
    public void AHighlighterStrokeIsOnePathHoweverManyPointsItHas()
    {
        var board = Started();
        Assert.True(board.SetTool(StrokeTool.Highlighter));
        Assert.True(board.Pen(100, 100, Down));
        Assert.True(board.Pen(20000, 100, Move));
        Assert.True(board.Pen(100, 100, Move));
        Assert.True(board.Pen(20000, 100, Move));
        Assert.True(board.Pen(100, 100, Up));

        var stroke = Assert.Single(board.Strokes);
        Assert.Equal(StrokeTool.Highlighter, stroke.Tool);
        Assert.Equal(5, stroke.Points.Count);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AShapeRunsFromTheDownPointToTheLatestMove(int toolId)
    {
        var tool = (StrokeTool)toolId;
        var board = Started();
        Assert.True(board.SetTool(tool));
        Assert.True(board.Pen(100, 200, Down));
        Assert.True(board.Pen(300, 400, Move));
        Assert.True(board.Pen(500, 600, Move));
        Assert.True(board.Pen(700, 800, Up));

        Assert.Equal(
            new[] { new NormPoint(100, 200), new NormPoint(700, 800) },
            Assert.Single(board.Strokes).Points);
    }

    [Fact]
    public void WidthsAreASharePerScreenHeightAndNeverBelowOnePixel()
    {
        Assert.Equal(4f, new Stroke(StrokeTool.Pen, 0).WidthPx(1000), 3);
        Assert.Equal(4f, new Stroke(StrokeTool.Arrow, 0).WidthPx(1000), 3);
        Assert.Equal(20f, new Stroke(StrokeTool.Highlighter, 0).WidthPx(1000), 3);
        Assert.Equal(1f, new Stroke(StrokeTool.Pen, 0).WidthPx(100), 3);
    }

    [Fact]
    public void PointsMapTheWholeSpanOntoTheScreen()
    {
        var stroke = new Stroke(StrokeTool.Pen, 0);
        stroke.Points.Add(new NormPoint(0, 0));
        stroke.Points.Add(new NormPoint(65535, 65535));

        var path = stroke.Path(1920, 1080);

        Assert.Equal(new Vector2(0, 0), path[0]);
        Assert.Equal(new Vector2(1919, 1079), path[1]);
    }

    [Fact]
    public void AnArrowHeadIsFourPenWidthsLongAndSitsBehindTheTip()
    {
        var stroke = new Stroke(StrokeTool.Arrow, 0);
        stroke.Points.Add(new NormPoint(0, 32768));
        stroke.Points.Add(new NormPoint(65535, 32768));

        var wings = stroke.ArrowWings(1000, 1000);
        var tip = stroke.Path(1000, 1000)[1];

        Assert.Equal(2, wings.Length);
        foreach (var wing in wings)
        {
            Assert.Equal(16f, Vector2.Distance(wing, tip), 2);
            Assert.True(wing.X < tip.X);
        }

        Assert.Equal(tip.Y, (wings[0].Y + wings[1].Y) / 2f, 2);
        Assert.NotEqual(wings[0].Y, wings[1].Y);
    }

    [Fact]
    public void OnlyAnArrowWithLengthHasAHead()
    {
        var line = new Stroke(StrokeTool.Line, 0);
        line.Points.Add(new NormPoint(0, 0));
        line.Points.Add(new NormPoint(900, 900));
        var still = new Stroke(StrokeTool.Arrow, 0);
        still.Points.Add(new NormPoint(5, 5));
        still.Points.Add(new NormPoint(5, 5));

        Assert.Empty(line.ArrowWings(1000, 1000));
        Assert.Empty(still.ArrowWings(1000, 1000));
        Assert.Empty(new Stroke(StrokeTool.Arrow, 0).ArrowWings(1000, 1000));
    }
}
