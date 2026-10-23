using System.Drawing;
using Edgepad.Annotation;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The signature PNG. System.Drawing draws without a desktop, so this runs on the CI runner like the rest.
/// </summary>
public sealed class SignatureExportTests
{
    private const byte Up = 0;
    private const byte Down = 1;
    private const byte Move = 2;

    // One pen line on a 1000 x 1000 screen, from (249.8, 249.8) to (749.3, 499.5) pixels: a pen is 4 px wide there.
    private static StrokeBoard OneLine(StrokeTool tool = StrokeTool.Pen, int colour = 0)
    {
        var board = new StrokeBoard();
        board.Begin(BoardMode.Annotate);
        Assert.True(board.SetTool(tool));
        Assert.True(board.SetColour(colour));
        Assert.True(board.Pen(16384, 16384, Down));
        Assert.True(board.Pen(49152, 32768, Move));
        Assert.True(board.Pen(49152, 32768, Up));
        return board;
    }

    private static Bitmap Decode(byte[] png) => new(new MemoryStream(png));

    [Fact]
    public void NoStrokesIsNothingToExport()
    {
        var board = new StrokeBoard();
        board.Begin(BoardMode.Sign);

        Assert.Null(SignatureExport.Render(board.Strokes, 1000, 1000));
        Assert.Null(SignatureExport.CropRect(board.Strokes, 1000, 1000));
    }

    [Fact]
    public void AStrokeStillUnderTheFingerIsNotExported()
    {
        var board = new StrokeBoard();
        board.Begin(BoardMode.Sign);
        Assert.True(board.Pen(100, 100, Down));

        Assert.Null(SignatureExport.Render(board.Strokes, 1000, 1000));
    }

    [Fact]
    public void ADegenerateScreenIsNothingToExport()
    {
        var board = OneLine();

        Assert.Null(SignatureExport.CropRect(board.Strokes, 0, 1000));
        Assert.Null(SignatureExport.Render(board.Strokes, 1000, 0));
    }

    [Fact]
    public void TheCropIsTheInkPlusHalfThePenPlusEightPixels()
    {
        // Ink x 247.8..751.3 and y 247.8..501.5, so floor/ceiling then 8 px more each way.
        var crop = SignatureExport.CropRect(OneLine().Strokes, 1000, 1000);

        Assert.Equal(Rectangle.FromLTRB(239, 239, 760, 510), crop);
    }

    [Fact]
    public void TheCropNeverLeavesTheScreen()
    {
        var board = new StrokeBoard();
        board.Begin(BoardMode.Sign);
        Assert.True(board.Pen(0, 0, Down));
        Assert.True(board.Pen(65535, 65535, Up));

        Assert.Equal(new Rectangle(0, 0, 1000, 1000), SignatureExport.CropRect(board.Strokes, 1000, 1000));
    }

    [Fact]
    public void TheCropCoversEveryStroke()
    {
        var board = OneLine();
        Assert.True(board.Pen(60000, 60000, Down));
        Assert.True(board.Pen(60000, 60000, Up));

        var crop = SignatureExport.CropRect(board.Strokes, 1000, 1000)!.Value;

        Assert.True(crop.Right > 915);
        Assert.True(crop.Bottom > 915);
        Assert.Equal(239, crop.Left);
    }

    [Fact]
    public void ThePngIsTransparentAtTheCornerAndInkedOnTheLine()
    {
        var png = SignatureExport.Render(OneLine(colour: 2).Strokes, 1000, 1000);

        Assert.NotNull(png);
        using var image = Decode(png);
        Assert.Equal(new Size(521, 271), image.Size);
        Assert.Equal(0, image.GetPixel(0, 0).A);
        Assert.Equal(0, image.GetPixel(image.Width - 1, image.Height - 1).A);

        // On screen the line passes (500.5, 375.1), which is (261.5, 136.1) in the crop; pixel row 135 is inside its 4 px band.
        var onLine = image.GetPixel(261, 135);
        Assert.Equal(255, onLine.A);
        Assert.Equal(Color.FromArgb(0xFF, 0x3B, 0x30), Color.FromArgb(onLine.R, onLine.G, onLine.B));
    }

    [Fact]
    public void TheHighlighterIsTranslucent()
    {
        var board = new StrokeBoard();
        board.Begin(BoardMode.Annotate);
        Assert.True(board.SetTool(StrokeTool.Highlighter));
        Assert.True(board.Pen(10000, 32768, Down));
        Assert.True(board.Pen(50000, 32768, Up));

        using var image = Decode(SignatureExport.Render(board.Strokes, 1000, 1000)!);

        // 0.35 of 255, rounded, in the middle of a 20 px band.
        Assert.Equal(89, image.GetPixel(image.Width / 2, image.Height / 2).A);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ShapesAreOutlinedAndLeaveTheirInsideEmpty(int toolId)
    {
        var board = OneLine((StrokeTool)toolId);

        using var image = Decode(SignatureExport.Render(board.Strokes, 1000, 1000)!);

        // A rectangle or ellipse is only its outline, so the middle of its box shows nothing; an arrow is a line
        // and head, so its middle is on the shaft and does not.
        var middle = image.GetPixel(image.Width / 2, image.Height / 2).A;
        Assert.Equal(toolId == 3, middle > 0);
    }

    [Fact]
    public void ATapIsADotNotNothing()
    {
        var board = new StrokeBoard();
        board.Begin(BoardMode.Sign);
        Assert.True(board.Pen(32768, 32768, Down));
        Assert.True(board.Pen(32768, 32768, Up));

        using var image = Decode(SignatureExport.Render(board.Strokes, 1000, 1000)!);

        Assert.Equal(255, image.GetPixel(image.Width / 2, image.Height / 2).A);
    }

    [Fact]
    public void TwoSavesInOneSecondKeepBothSignatures()
    {
        // Same stem, as two saves within one second get: the second must take a new name, never the first's file.
        var folder = Path.Combine(Path.GetTempPath(), $"edgepad-signatures-{Guid.NewGuid():N}");
        try
        {
            byte[] one = [1];
            byte[] two = [2];
            var first = Annotator.WriteNew(folder, "signature-20261024-120000", one);
            var second = Annotator.WriteNew(folder, "signature-20261024-120000", two);

            Assert.Equal(Path.Combine(folder, "signature-20261024-120000.png"), first);
            Assert.Equal(Path.Combine(folder, "signature-20261024-120000-2.png"), second);
            Assert.Equal(one, File.ReadAllBytes(first));
            Assert.Equal(two, File.ReadAllBytes(second));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
