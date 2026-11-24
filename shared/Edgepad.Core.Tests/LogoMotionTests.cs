using Xunit;

namespace Edgepad.Tests;

/// <summary>The pop's two ends: every part small and unseen at 0 ms, full size and opaque at 760 ms.</summary>
public sealed class LogoMotionTests
{
    [Fact]
    public void StartsSmallAndInvisible()
    {
        foreach (var pose in LogoMotion.Frame(0))
        {
            Assert.Equal(0.55, pose.Scale, 9);
            Assert.Equal(0, pose.Opacity, 9);
        }
    }

    [Fact]
    public void EndsFullSizeAndOpaque()
    {
        var poses = LogoMotion.Frame(LogoMotion.TotalMs);
        Assert.Equal(LogoMotion.Strokes.Count, poses.Length);
        foreach (var pose in poses)
        {
            Assert.Equal(1, pose.Scale, 9);
            Assert.Equal(1, pose.Opacity, 9);
        }
    }
}
