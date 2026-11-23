using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class MotionTests
{
    [Fact]
    public void TheThreeTiersAreDefinedOnce()
    {
        Assert.Equal(160, Theme.Motion.ScaledFor(true, Theme.Motion.PressMs));
        Assert.Equal(300, Theme.Motion.ScaledFor(true, Theme.Motion.RevealMs));
        Assert.Equal(400, Theme.Motion.ScaledFor(true, Theme.Motion.SceneMs));
    }

    [Theory]
    [InlineData(Theme.Motion.PressMs)]
    [InlineData(Theme.Motion.RevealMs)]
    [InlineData(Theme.Motion.SceneMs)]
    public void EveryTierIsZeroWhileAnimationsAreOff(int milliseconds) =>
        Assert.Equal(0, Theme.Motion.ScaledFor(false, milliseconds));

    [Fact]
    public void ProgressRunsFromZeroToOneAndStaysClamped()
    {
        Assert.Equal(0, Progress.Of(0, 300));
        Assert.Equal(0, Progress.Of(-50, 300));
        Assert.Equal(1, Progress.Of(300, 300));
        Assert.Equal(1, Progress.Of(900, 300));
        Assert.Equal(1, Progress.Of(0, 0));
    }

    [Fact]
    public void ProgressEasesOutSoHalfwayIsPastHalf()
    {
        var half = Progress.Of(150, 300);

        Assert.InRange(half, 0.5, 1);
        Assert.Equal(0.875, half, 6);
    }

    [Fact]
    public void LeavingEasesInSoHalfwayIsBeforeHalf()
    {
        Assert.Equal(0, Progress.Leaving(0, 300));
        Assert.Equal(1, Progress.Leaving(900, 300));
        Assert.Equal(1, Progress.Leaving(0, 0));
        Assert.Equal(0.125, Progress.Leaving(150, 300), 6);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 100)]
    [InlineData(1, 200)]
    [InlineData(-1, 0)]
    [InlineData(2, 200)]
    public void TheClipNeverLeavesTheMenu(double progress, int expected) =>
        Assert.Equal(expected, Reveal.ClipHeight(progress, 200));
}
