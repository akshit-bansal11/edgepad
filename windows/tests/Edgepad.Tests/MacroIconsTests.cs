using System.Globalization;
using System.Text;
using Edgepad.Macros;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The two halves of macro icons a build machine can actually reach: which extraction path a target takes,
/// and the chunk format that carries the result to the phone.
///
/// Nothing here extracts a real icon. The calls behind the branch are Win32 and a runner has no program
/// icons worth reading, so the branch is what gets pinned — a .png routed down the document path would put
/// the image viewer's icon on the button, which reads as a broken extractor rather than a wrong case.
///
/// The chunking is the opposite: pure, and the half of a wire format whose other half is on the phone.
/// </summary>
public sealed class MacroIconsTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-icons-" + Guid.NewGuid().ToString("N"));

    public MacroIconsTests() => Directory.CreateDirectory(dir);

    [Fact]
    public void AProgramCarriesItsOwnIcon() =>
        Assert.Equal(MacroIcons.Source.Resource, MacroIcons.SourceFor(Make("app.exe")));

    [Fact]
    public void AnIconFileIsReadTheSameWayAProgramIs() =>
        Assert.Equal(MacroIcons.Source.Resource, MacroIcons.SourceFor(Make("mark.ico")));

    [Fact]
    public void ThatExtensionIsMatchedWhateverItsCase() =>
        Assert.Equal(MacroIcons.Source.Resource, MacroIcons.SourceFor(Make("APP.EXE")));

    [Fact]
    public void APictureIsTheIconRatherThanHavingOne() =>
        Assert.Equal(MacroIcons.Source.Picture, MacroIcons.SourceFor(Make("logo.png")));

    [Fact]
    public void ADocumentBorrowsItsFileTypesIcon() =>
        Assert.Equal(MacroIcons.Source.Associated, MacroIcons.SourceFor(Make("notes.txt")));

    [Fact]
    public void AnExtensionlessFileIsStillADocument() =>
        Assert.Equal(MacroIcons.Source.Associated, MacroIcons.SourceFor(Make("README")));

    [Fact]
    public void AUrlHasNoIconToRead() =>
        Assert.Equal(MacroIcons.Source.None, MacroIcons.SourceFor("https://mail.google.com"));

    [Fact]
    public void AFolderHasNoIconToRead() =>
        Assert.Equal(MacroIcons.Source.None, MacroIcons.SourceFor(dir));

    [Fact]
    public void APathThatIsNotThereHasNoIconToRead() =>
        Assert.Equal(MacroIcons.Source.None, MacroIcons.SourceFor(Path.Combine(dir, "gone.exe")));

    [Fact]
    public void NothingAtAllHasNoIconToRead()
    {
        Assert.Equal(MacroIcons.Source.None, MacroIcons.SourceFor(null));
        Assert.Equal(MacroIcons.Source.None, MacroIcons.SourceFor(string.Empty));
    }

    [Fact]
    public void AMacroWithNothingReadableHasNoPicture()
    {
        // The whole fallback chain in one: an override that is not there falls through to the target, and a
        // target that is a URL falls through to no icon — a button with a label, never an error.
        Assert.Null(MacroIcons.Png(new Macro("Mail", "https://mail.google.com", null)));
        Assert.Null(MacroIcons.Png(new Macro("Mail", "https://mail.google.com", null, Path.Combine(dir, "gone.png"))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(179)]
    [InlineData(180)]
    [InlineData(181)]
    [InlineData(MacroIcons.ChunkBytes - 1)]
    [InlineData(MacroIcons.ChunkBytes)]
    [InlineData(MacroIcons.ChunkBytes + 1)]
    [InlineData(MacroIcons.MaxBytes)]
    public void EveryChunkFitsOneTextPayload(int size)
    {
        // The sizes either side of a chunk boundary are the ones worth naming: an off-by-one in the ceiling
        // division shows up as a trailing empty frame or a lost tail, and only at exactly these lengths.
        var chunks = MacroIcons.Chunks(MacroStore.MaxMacros - 1, Bytes(size));

        Assert.All(chunks, chunk => Assert.True(
            Encoding.UTF8.GetByteCount(chunk) <= FrameCodec.MaxTextBytes,
            $"{Encoding.UTF8.GetByteCount(chunk)} bytes is past what a TEXT payload's length byte can say"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(MacroIcons.ChunkBytes)]
    [InlineData(MacroIcons.ChunkBytes + 1)]
    [InlineData(MacroIcons.MaxBytes)]
    public void TheChunksRejoinIntoExactlyWhatWentIn(int size)
    {
        // The phone's half of this format, written out here so both halves are pinned by something. If this
        // and the Kotlin twin ever disagree, an icon arrives as a grey box and nothing says why.
        var png = Bytes(size);

        var joined = string.Concat(MacroIcons.Chunks(3, png).Select(chunk => chunk.Split('/', 4)[3]));

        Assert.Equal(png, Convert.FromBase64String(joined));
    }

    [Fact]
    public void EachChunkNamesItsSlotAndHowManyThereAre()
    {
        var chunks = MacroIcons.Chunks(7, Bytes(MacroIcons.ChunkBytes * 2));

        Assert.All(chunks, chunk =>
        {
            var header = chunk.Split('/', 4);
            Assert.Equal("7", header[0]);
            Assert.Equal(chunks.Count.ToString(CultureInfo.InvariantCulture), header[2]);
        });
        var counted = Enumerable.Range(0, chunks.Count).Select(i => i.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(counted, chunks.Select(chunk => chunk.Split('/', 4)[1]));
    }

    [Fact]
    public void AnIconOfNothingIsNoFramesRatherThanOneEmptyOne()
    {
        // An empty frame would reach the phone as a finished icon of nothing, which draws as a hole in the
        // grid. Png never returns an empty array, so this pins the floor rather than a case that happens.
        Assert.Empty(MacroIcons.Chunks(0, []));
    }

    /// <summary>Bytes that do not compress to nothing, so a size asked for is a size encoded.</summary>
    private static byte[] Bytes(int size) => [.. Enumerable.Range(0, size).Select(i => (byte)(i * 31 % 251))];

    /// <summary>An empty file with the given name. Only its path and its extension are ever looked at.</summary>
    private string Make(string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
