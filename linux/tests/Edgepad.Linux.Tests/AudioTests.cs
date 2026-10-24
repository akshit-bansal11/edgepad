using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class AudioTests
{
    [Theory]
    [InlineData("Volume: 0.55", 55, false)]
    [InlineData("Volume: 0.55\n", 55, false)]
    [InlineData("Volume: 0.55 [MUTED]", 55, true)]
    [InlineData("Volume: 0.00", 0, false)]
    [InlineData("Volume: 1.00", 100, false)]
    // 0.555 rounds away from zero; the nearest whole percent, as the dial shows.
    [InlineData("Volume: 0.555", 56, false)]
    // Above 100 % is reported as the top of the dial.
    [InlineData("Volume: 1.20", 100, false)]
    [InlineData("Volume: 1.50 [MUTED]", 100, true)]
    public void WpctlVolumeIsParsed(string output, int percent, bool muted) =>
        Assert.Equal(new Level(percent, muted), Audio.ParseWpctl(output));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("Volume:")]
    [InlineData("Volume: loud")]
    [InlineData("Volume: -0.5")]
    [InlineData("Volume: NaN")]
    [InlineData("Could not connect to PipeWire")]
    // Not wpctl's own line: pactl's.
    [InlineData("Mute: yes")]
    public void WpctlGarbageIsNull(string? output) => Assert.Null(Audio.ParseWpctl(output));

    [Fact]
    public void PactlTakesTheFirstChannelAndTheMuteLine()
    {
        const string Volume = "Volume: front-left: 36044 /  55% / -15.18 dB,   front-right: 36044 /  55% / -15.18 dB\n"
            + "        balance 0.00\n";
        Assert.Equal(new Level(55, false), Audio.ParsePactl(Volume, "Mute: no\n"));
        Assert.Equal(new Level(55, true), Audio.ParsePactl(Volume, "Mute: yes\n"));
    }

    [Fact]
    public void PactlAboveTheTopIsClampedAndAMissingMuteLineIsNotMuted()
    {
        Assert.Equal(new Level(100, false), Audio.ParsePactl("Volume: mono: 98304 / 150% / 10.57 dB", null));
        Assert.Equal(new Level(0, false), Audio.ParsePactl("Volume: mono: 0 /   0% / -inf dB", "huh"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("%")]
    [InlineData("Volume: lots%")]
    public void PactlGarbageIsNull(string? volume) => Assert.Null(Audio.ParsePactl(volume, "Mute: no"));
}
