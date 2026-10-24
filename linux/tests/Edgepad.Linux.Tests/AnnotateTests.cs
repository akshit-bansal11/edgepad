using Avalonia;
using Edgepad.Annotation;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The parts of annotate and sign that need no display: the crop, the file names, the Pictures folder, the clipboard
/// tools and what TEXT 9 offers. Rendering the PNG needs Avalonia's renderer, which needs a platform the CI runner
/// does not have without the Avalonia.Headless package, so the drawing itself is not tested here.
/// </summary>
public sealed class AnnotateTests
{
    private const byte Up = 0;
    private const byte Down = 1;
    private const byte Move = 2;

    // One pen line on a 1000 x 1000 screen, from (249.8, 249.8) to (749.3, 499.5) pixels: a pen is 4 px wide there.
    // The Windows app's SignatureExportTests use the same line, so both crops are held to the same numbers.
    private static StrokeBoard OneLine()
    {
        var board = new StrokeBoard();
        board.Begin(BoardMode.Sign);
        Assert.True(board.Pen(16384, 16384, Down));
        Assert.True(board.Pen(49152, 32768, Move));
        Assert.True(board.Pen(49152, 32768, Up));
        return board;
    }

    [Fact]
    public void NoStrokesIsNothingToCrop()
    {
        var board = new StrokeBoard();
        board.Begin(BoardMode.Sign);

        Assert.Null(SignatureExport.CropRect(board.Strokes, 1000, 1000));
    }

    [Fact]
    public void AStrokeStillUnderTheFingerIsNotCropped()
    {
        var board = new StrokeBoard();
        board.Begin(BoardMode.Sign);
        Assert.True(board.Pen(100, 100, Down));

        Assert.Null(SignatureExport.CropRect(board.Strokes, 1000, 1000));
    }

    [Fact]
    public void ADegenerateScreenIsNothingToCrop()
    {
        var board = OneLine();

        Assert.Null(SignatureExport.CropRect(board.Strokes, 0, 1000));
        Assert.Null(SignatureExport.CropRect(board.Strokes, 1000, 0));
    }

    [Fact]
    public void TheCropIsTheInkPlusHalfThePenPlusEightPixels()
    {
        // Ink x 247.8..751.3 and y 247.8..501.5, so floor/ceiling then 8 px more each way: 239..760 by 239..510.
        Assert.Equal(new PixelRect(239, 239, 521, 271), SignatureExport.CropRect(OneLine().Strokes, 1000, 1000));
    }

    [Fact]
    public void TheCropNeverLeavesTheScreen()
    {
        var board = new StrokeBoard();
        board.Begin(BoardMode.Sign);
        Assert.True(board.Pen(0, 0, Down));
        Assert.True(board.Pen(65535, 65535, Up));

        Assert.Equal(new PixelRect(0, 0, 1000, 1000), SignatureExport.CropRect(board.Strokes, 1000, 1000));
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
        Assert.Equal(239, crop.X);
    }

    [Fact]
    public void TwoSavesInOneSecondKeepEverySignature()
    {
        // Same stem, as saves within one second get: each takes a new name, never an earlier one's file.
        var folder = Path.Combine(Path.GetTempPath(), $"edgepad-signatures-{Guid.NewGuid():N}");
        try
        {
            byte[] one = [1];
            byte[] two = [2];
            byte[] three = [3];
            var first = SignatureExport.WriteNew(folder, "signature-20261024-120000", one);
            var second = SignatureExport.WriteNew(folder, "signature-20261024-120000", two);
            var third = SignatureExport.WriteNew(folder, "signature-20261024-120000", three);

            Assert.Equal(Path.Combine(folder, "signature-20261024-120000.png"), first);
            Assert.Equal(Path.Combine(folder, "signature-20261024-120000-2.png"), second);
            Assert.Equal(Path.Combine(folder, "signature-20261024-120000-3.png"), third);
            Assert.Equal(one, File.ReadAllBytes(first));
            Assert.Equal(two, File.ReadAllBytes(second));
            Assert.Equal(three, File.ReadAllBytes(third));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheStemIsTheLocalTimeToTheSecond() =>
        Assert.Equal("signature-20261024-090507", SignatureExport.Stem(new DateTime(2026, 10, 24, 9, 5, 7)));

    [Fact]
    public void AnAbsolutePicturesVariableWins() =>
        Assert.Equal("/data/pics", SignatureExport.Pictures("/data/pics", "XDG_PICTURES_DIR=\"$HOME/Bilder\"", "/home/me"));

    [Fact]
    public void UserDirsUnderHomeIsReadFromTheFile()
    {
        const string Dirs = "# written by xdg-user-dirs-update\nXDG_DESKTOP_DIR=\"$HOME/Desktop\"\nXDG_PICTURES_DIR=\"$HOME/Bilder\"\n";

        Assert.Equal(Path.Combine("/home/me", "Bilder"), SignatureExport.Pictures(null, Dirs, "/home/me"));
    }

    [Fact]
    public void AnAbsoluteUserDirIsTakenAsItIs() =>
        Assert.Equal("/srv/pics", SignatureExport.Pictures("", "XDG_PICTURES_DIR=\"/srv/pics\"", "/home/me"));

    [Theory]
    // No file, no line, a commented-out line, a relative path, and the home folder itself (the spec's "turned off").
    [InlineData(null)]
    [InlineData("XDG_DESKTOP_DIR=\"$HOME/Desktop\"")]
    [InlineData("# XDG_PICTURES_DIR=\"$HOME/Bilder\"")]
    [InlineData("XDG_PICTURES_DIR=\"Bilder\"")]
    [InlineData("XDG_PICTURES_DIR=\"$HOME/\"")]
    [InlineData("XDG_PICTURES_DIR=\"$HOME\"")]
    public void OtherwiseThePicturesFolderUnderHome(string? dirs) =>
        Assert.Equal(Path.Combine("/home/me", "Pictures"), SignatureExport.Pictures(null, dirs, "/home/me"));

    [Fact]
    public void BothSessionsTryWlCopyThenXclipForAPng()
    {
        var tools = SignatureExport.ImageTools("wayland-0", ":0");

        Assert.Equal(2, tools.Count);
        Assert.Equal("wl-copy", tools[0].Program);
        string[] wayland = ["--type", "image/png"];
        Assert.Equal(wayland, tools[0].Arguments);
        Assert.Equal("xclip", tools[1].Program);
        string[] x11 = ["-selection", "clipboard", "-t", "image/png"];
        Assert.Equal(x11, tools[1].Arguments);
    }

    [Fact]
    public void X11AloneUsesXclip() =>
        Assert.Equal("xclip", Assert.Single(SignatureExport.ImageTools(null, ":0")).Program);

    [Fact]
    public void NoDisplayNoImageTool()
    {
        Assert.Empty(SignatureExport.ImageTools(null, null));
        Assert.Empty(SignatureExport.ImageTools("", ""));
    }

    [Fact]
    public void TheFeatureTokens()
    {
        Assert.Equal("annotate/capture", Annotator.Features(annotate: true));
        Assert.Equal("capture", Annotator.Features(annotate: false));
    }

    [Fact]
    public void AnX11WindowThatIsSeeThroughAndClickThroughOffersAnnotate() =>
        Assert.Equal("annotate/capture", Annotator.Features(Annotator.Refusal("XID", transparent: true, () => true) is null));

    [Theory]
    // Not an X11 window: another backend, or no handle at all.
    [InlineData("HWND")]
    [InlineData(null)]
    public void AnythingButX11OffersCaptureOnlyAndNeverShapes(string? descriptor)
    {
        var refusal = Annotator.Refusal(descriptor, transparent: true, () => throw new InvalidOperationException("shaped a window that is not X11"));

        Assert.NotNull(refusal);
        Assert.Equal("capture", Annotator.Features(refusal is null));
    }

    [Fact]
    public void NoCompositorOffersCaptureOnly() =>
        Assert.NotNull(Annotator.Refusal("XID", transparent: false, () => throw new InvalidOperationException("shaped an opaque window")));

    [Fact]
    public void NoInputShapeOffersCaptureOnly() =>
        Assert.NotNull(Annotator.Refusal("XID", transparent: true, () => false));
}
