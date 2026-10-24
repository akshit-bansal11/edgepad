using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class BacklightTests
{
    [Theory]
    [InlineData(0, 255, 0)]
    [InlineData(255, 255, 100)]
    [InlineData(128, 255, 50)]
    // 1/255 is 0.39 %, rounded to the nearest whole percent.
    [InlineData(1, 255, 0)]
    [InlineData(2, 255, 1)]
    [InlineData(96000, 96000, 100)]
    // A reading above the range (a firmware that overshoots) stays on the dial.
    [InlineData(300, 255, 100)]
    public void PercentIsTheNearestWholePercent(long value, long max, int expected) =>
        Assert.Equal(expected, Backlight.Percent(value, max));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ARangeOfNothingHasNoPercent(long max) => Assert.Null(Backlight.Percent(5, max));

    [Theory]
    [InlineData(100, 255, 255)]
    [InlineData(50, 255, 128)]
    [InlineData(10, 100, 10)]
    [InlineData(150, 255, 255)]
    [InlineData(-5, 255, 1)]
    // Never fully dark: 0 % is the dimmest step, not off.
    [InlineData(0, 255, 1)]
    [InlineData(0, 1000, 1)]
    public void RawScalesAndNeverGoesDark(int percent, long max, long expected) =>
        Assert.Equal(expected, Backlight.Raw(percent, max));

    [Fact]
    public void RawWithNoRangeIsZero() => Assert.Equal(0, Backlight.Raw(50, 0));

    [Fact]
    public void ReadsTheFirstDeviceThatHasARangeFromAFolder()
    {
        var root = Directory.CreateTempSubdirectory("edgepad-backlight").FullName;
        try
        {
            Device(root, "a_empty", 0, 0);
            Device(root, "b_panel", 50, 200);
            Device(root, "c_other", 1, 10);
            using var backlight = new Backlight(root);
            Assert.True(backlight.Available);
            Assert.Equal(25, backlight.Read());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NoFolderMeansNoBacklight()
    {
        using var backlight = new Backlight(Path.Combine(Path.GetTempPath(), "edgepad-no-such-folder"));
        Assert.False(backlight.Available);
        Assert.Null(backlight.Read());
        Assert.False(backlight.Set(50));
    }

    private static void Device(string root, string name, int brightness, int max)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        File.WriteAllText(Path.Combine(folder, "brightness"), $"{brightness}\n");
        File.WriteAllText(Path.Combine(folder, "max_brightness"), $"{max}\n");
    }
}
