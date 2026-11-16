using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class DisplayModesTests
{
    [Fact]
    public void OneMonitorListsItsRatesAtTheCurrentResolution()
    {
        const string text = """
            Screen 0: minimum 320 x 200, current 2560 x 1440, maximum 16384 x 16384
            DP-1 connected primary 2560x1440+0+0 (normal left inverted right x axis y axis) 600mm x 340mm
               2560x1440    143.97 +  59.95*
               1920x1080    144.00    60.00
               1280x720      60.00
            HDMI-1 disconnected (normal left inverted right x axis y axis)
            """;

        var output = XrandrParse.Parse(text);

        Assert.NotNull(output);
        Assert.Equal("DP-1", output.Name);
        Assert.Equal("2560x1440", output.Mode);
        Assert.Equal([60, 144], output.State.Rates);
        Assert.Equal(0, output.State.Current);
    }

    [Fact]
    public void ThePrimaryOutputWinsEvenWhenItComesSecond()
    {
        const string text = """
            Screen 0: minimum 320 x 200, current 4480 x 1440, maximum 16384 x 16384
            HDMI-1 connected 1920x1080+0+0 (normal left inverted right x axis y axis) 510mm x 290mm
               1920x1080     60.00*+  50.00
            DP-1 connected primary 2560x1440+1920+0 (normal left inverted right x axis y axis) 600mm x 340mm
               2560x1440    143.97*+ 59.95
            """;

        var output = XrandrParse.Parse(text);

        Assert.NotNull(output);
        Assert.Equal("DP-1", output.Name);
        Assert.Equal([60, 144], output.State.Rates);
        Assert.Equal(1, output.State.Current);
    }

    [Fact]
    public void AnInterlacedModeIsLeftOut()
    {
        const string text = """
            Screen 0: minimum 320 x 200, current 1920 x 1080, maximum 16384 x 16384
            HDMI-1 connected primary 1920x1080+0+0 (normal left inverted right x axis y axis) 510mm x 290mm
               1920x1080i     75.00    50.00
               1920x1080     120.00*+  60.00
            """;

        var output = XrandrParse.Parse(text);

        Assert.NotNull(output);
        Assert.Equal([60, 120], output.State.Rates);
        Assert.Equal(1, output.State.Current);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Screen 0: minimum 320 x 200, current 0 x 0, maximum 16384 x 16384\nDP-1 disconnected (normal)\n")]
    public void NothingConnectedGivesNothing(string text) => Assert.Null(XrandrParse.Parse(text));

    [Fact]
    public void MutterRatesComeFromTheModesAtTheCurrentSize()
    {
        var config = new MutterConfig(
            7,
            [
                new MutterMonitor("eDP-1", [
                    new MutterMode("1920x1080@60.002", 1920, 1080, 60.002, true, false),
                    new MutterMode("1920x1080@144.0", 1920, 1080, 144.0, false, false),
                    new MutterMode("1920x1080@75.0i", 1920, 1080, 75.0, false, true),
                    new MutterMode("1280x720@90.0", 1280, 720, 90.0, false, false),
                ]),
            ],
            [new MutterLogical(0, 0, 1, 0, true, ["eDP-1"])]);

        var state = config.Describe();

        Assert.Equal([60, 144], state.Rates);
        Assert.Equal(0, state.Current);
        Assert.Equal("1920x1080@144.0", config.ModeFor("eDP-1", 144));
        Assert.Null(config.ModeFor("eDP-1", 90));
    }
}
