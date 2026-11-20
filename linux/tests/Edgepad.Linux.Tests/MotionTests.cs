using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>The three motion tiers, and how the desktop's animations setting maps to them.</summary>
public sealed class MotionTests
{
    [Fact]
    public void TheTiersArePressRevealAndScene()
    {
        Assert.Equal(160, Motion.Press);
        Assert.Equal(300, Motion.Reveal);
        Assert.Equal(400, Motion.Scene);
    }

    [Theory]
    [InlineData("true\n", true)]
    [InlineData("false\n", false)]
    [InlineData("false", false)]
    [InlineData("", true)]
    [InlineData("garbage", true)]
    public void TheSettingMapsToEnabled(string answer, bool expected) =>
        Assert.Equal(expected, Motion.EnabledFrom(() => answer));

    [Fact]
    public void AnUnreadableSettingAnimates() =>
        Assert.True(Motion.EnabledFrom(() => null));
}
