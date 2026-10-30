using System.Globalization;
using System.Text;
using Edgepad.Macros;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The chunk format that carries a macro icon to the phone. Pure, and the half of a wire format whose other
/// half is on the phone, so it is tested here in the core that both the Windows and Linux apps share: a
/// format pinned in only one of them could still drift in the other.
/// </summary>
public sealed class MacroIconChunksTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(179)]
    [InlineData(180)]
    [InlineData(181)]
    [InlineData(MacroIconChunks.ChunkBytes - 1)]
    [InlineData(MacroIconChunks.ChunkBytes)]
    [InlineData(MacroIconChunks.ChunkBytes + 1)]
    [InlineData(24 * 1024)]
    [InlineData(96 * 1024)]
    public void EveryChunkFitsOneTextPayload(int size)
    {
        // The sizes either side of a chunk boundary are the ones worth naming: an off-by-one in the ceiling
        // division shows up as a trailing empty frame or a lost tail, and only at exactly these lengths.
        var chunks = MacroIconChunks.Chunks(MacroStore.MaxMacros - 1, Bytes(size));

        Assert.All(chunks, chunk => Assert.True(
            Encoding.UTF8.GetByteCount(chunk) <= FrameCodec.MaxTextBytes,
            $"{Encoding.UTF8.GetByteCount(chunk)} bytes is past what a TEXT payload's length byte can say"));
    }

    [Fact]
    public void TheHeaviestDefaultIconStaysUnderTheCeilingThePhoneWillAccept()
    {
        // The phone refuses a chunk count over MAX_CHUNKS in LaptopState.kt and drops the frame, so an icon
        // past that ceiling does not arrive smaller -- it does not arrive at all, and nothing says why.
        // The two numbers are in different languages with no fixture between them, so this is what holds
        // them together: raising the default size's budget without raising MAX_CHUNKS fails here. This is the
        // ceiling of a phone that sends an empty WANT_ICONS, which can never go up: only a phone that asks for
        // more can be sent more.
        const int phoneMaxChunks = 160;

        var bytes = Bytes(MacroIconChunks.MaxBytes(MacroIconChunks.DefaultSize));
        var chunks = MacroIconChunks.Chunks(MacroStore.MaxMacros - 1, bytes).Count;

        Assert.True(
            chunks <= phoneMaxChunks,
            $"an icon at the cap is {chunks} chunks and the phone accepts {phoneMaxChunks}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(MacroIconChunks.ChunkBytes)]
    [InlineData(MacroIconChunks.ChunkBytes + 1)]
    [InlineData(96 * 1024)]
    public void TheChunksRejoinIntoExactlyWhatWentIn(int size)
    {
        // The phone's half of this format, written out here so both halves are pinned by something. If this
        // and the Kotlin twin ever disagree, an icon arrives as a grey box and nothing says why.
        var png = Bytes(size);

        var joined = string.Concat(MacroIconChunks.Chunks(3, png).Select(chunk => chunk.Split('/', 4)[3]));

        Assert.Equal(png, Convert.FromBase64String(joined));
    }

    [Fact]
    public void EachChunkNamesItsSlotAndHowManyThereAre()
    {
        var chunks = MacroIconChunks.Chunks(7, Bytes(MacroIconChunks.ChunkBytes * 2));

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
        // grid. An icon is never an empty array in practice, so this pins the floor rather than a case that happens.
        Assert.Empty(MacroIconChunks.Chunks(0, []));
    }

    [Theory]
    [InlineData("", 512, 128)]
    [InlineData(null, 512, 128)]
    [InlineData("abc", 512, 128)]
    [InlineData("0", 512, 128)]
    [InlineData("-5", 512, 128)]
    [InlineData("99999999999", 512, 128)]
    [InlineData("1", 512, 64)]
    [InlineData("64", 512, 64)]
    [InlineData("96", 512, 128)]
    [InlineData("128", 512, 128)]
    [InlineData("200", 512, 256)]
    [InlineData("385", 512, 512)]
    [InlineData("512", 512, 512)]
    [InlineData("9999", 512, 512)]
    [InlineData("512", 256, 256)]
    [InlineData("9999", 256, 256)]
    [InlineData("", 256, 128)]
    public void APayloadSnapsToATier(string? payload, int max, int expected) =>
        Assert.Equal(expected, MacroIconChunks.Snap(payload, max));

    [Theory]
    [InlineData(512, new[] { 512, 384, 256, 128, 64 })]
    [InlineData(256, new[] { 256, 128, 64 })]
    [InlineData(128, new[] { 128, 64 })]
    [InlineData(64, new[] { 64 })]
    public void TheChainRunsFromTheSizeDownToTheSmallest(int size, int[] expected) =>
        Assert.Equal(expected, MacroIconChunks.FallbackChain(size).ToArray());

    [Theory]
    [InlineData(64, 24 * 1024)]
    [InlineData(128, 24 * 1024)]
    [InlineData(256, 48 * 1024)]
    [InlineData(384, 72 * 1024)]
    [InlineData(512, 96 * 1024)]
    public void TheBudgetGrowsWithTheSideAndNeverFallsBelowTheDefaults(int size, int expected) =>
        Assert.Equal(expected, MacroIconChunks.MaxBytes(size));

    [Fact]
    public void TheHeaderStaysInsideTheMarginAtTheHeaviestTier()
    {
        // The margin ChunkBytes leaves: past eleven characters, a chunk of the heaviest icon is a truncated icon.
        var chunks = MacroIconChunks.Chunks(MacroStore.MaxMacros - 1, Bytes(MacroIconChunks.MaxBytes(512)));

        Assert.True(chunks.Count < 1000, $"{chunks.Count} chunks");
        Assert.All(chunks, chunk => Assert.True(
            chunk.Length - chunk.Split('/', 4)[3].Length <= 11,
            $"the header of {chunk[..16]} is past eleven characters"));
    }

    /// <summary>Bytes that do not compress to nothing, so a size asked for is a size encoded.</summary>
    private static byte[] Bytes(int size) => [.. Enumerable.Range(0, size).Select(i => (byte)(i * 31 % 251))];
}
