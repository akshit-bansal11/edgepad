using Edgepad.Macros;
using Edgepad.Platform;
using SkiaSharp;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// Rendering a macro's icon file to the PNG the phone is sent, with real native Skia. Each input is built here rather
/// than kept as a fixture, so what the test feeds in is readable next to what it expects. If native Skia cannot load
/// on the runner these fail loudly, which is the point: the app would fail the same way.
/// </summary>
public sealed class LinuxMacroIconsTests : IDisposable
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];

    private readonly string root = Path.Combine(Path.GetTempPath(), "edgepad-macroicons-" + Guid.NewGuid().ToString("N"));

    public LinuxMacroIconsTests() => Directory.CreateDirectory(root);

    [Fact]
    public void RenderLetterboxesAWidePngIntoTheSquare()
    {
        var path = Path.Combine(root, "wide.png");
        using (var bitmap = new SKBitmap(40, 20))
        {
            bitmap.Erase(SKColors.Red);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, data.ToArray());
        }

        var png = LinuxMacroIcons.Render(path);

        Assert.NotNull(png);
        Assert.Equal(PngSignature, png[..4]);
        using var decoded = SKBitmap.Decode(png);
        Assert.NotNull(decoded);
        Assert.Equal(MacroIconChunks.Size, decoded.Width);
        Assert.Equal(MacroIconChunks.Size, decoded.Height);
        var middle = decoded.GetPixel(MacroIconChunks.Size / 2, MacroIconChunks.Size / 2);
        Assert.Equal(255, middle.Alpha);
        Assert.True(middle.Red > 200 && middle.Green < 50 && middle.Blue < 50, middle.ToString());
        Assert.Equal(0, decoded.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void RenderDrawsAnSvg()
    {
        var path = Path.Combine(root, "green.svg");
        File.WriteAllText(
            path,
            """<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><rect width="10" height="10" fill="#00ff00"/></svg>""");

        var png = LinuxMacroIcons.Render(path);

        Assert.NotNull(png);
        using var decoded = SKBitmap.Decode(png);
        Assert.NotNull(decoded);
        Assert.Equal(MacroIconChunks.Size, decoded.Width);
        Assert.Equal(MacroIconChunks.Size, decoded.Height);
        var centre = decoded.GetPixel(MacroIconChunks.Size / 2, MacroIconChunks.Size / 2);
        Assert.Equal(255, centre.Alpha);
        Assert.True(centre.Green > 200 && centre.Red < 50 && centre.Blue < 50, centre.ToString());
    }

    [Fact]
    public void RenderOfATextFileNamedPngIsNull()
    {
        var path = Path.Combine(root, "not-a-picture.png");
        File.WriteAllText(path, "this is text");

        Assert.Null(LinuxMacroIcons.Render(path));
    }

    [Fact]
    public void RenderOfAFileOverTheByteCapIsNull()
    {
        // Sparse, so the test writes almost nothing to disk.
        var path = Path.Combine(root, "huge.png");
        using (var stream = new FileStream(path, FileMode.CreateNew))
        {
            stream.SetLength(LinuxMacroIcons.MaxSourceBytes + 1);
        }

        Assert.Null(LinuxMacroIcons.Render(path));
    }

    [Fact]
    public void RenderOfAPictureDeclaringHugeDimensionsIsNull()
    {
        // A BMP header alone: 54 bytes claiming 5000 x 5000 at 24 bits, no pixels. BMP rather than PNG because its
        // header carries no checksum, so it can be written out here by hand. Without the side cap, decoding would
        // allocate the whole bitmap and hand back a picture of the missing rows.
        var path = Path.Combine(root, "claims-huge.bmp");
        const int side = LinuxMacroIcons.MaxSourceSide + 904;
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write((byte)'B');
            writer.Write((byte)'M');
            writer.Write(54 + (side * 3 * side));
            writer.Write(0);
            writer.Write(54);
            writer.Write(40);
            writer.Write(side);
            writer.Write(side);
            writer.Write((short)1);
            writer.Write((short)24);
            writer.Write(0);
            writer.Write(0);
            writer.Write(2835);
            writer.Write(2835);
            writer.Write(0);
            writer.Write(0);
        }

        Assert.Null(LinuxMacroIcons.Render(path));
    }

    [Fact]
    public void PngOfAUrlIsNull() => Assert.Null(LinuxMacroIcons.Png(new Macro("Mail", "https://mail.google.com", null)));

    [Fact]
    public void PngOfAMissingOverrideOnAUrlIsNull() =>
        Assert.Null(LinuxMacroIcons.Png(new Macro("Mail", "https://mail.google.com", null, Path.Combine(root, "missing.png"))));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
