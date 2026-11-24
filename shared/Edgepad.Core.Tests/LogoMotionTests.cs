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

    [Fact]
    public void TileStartsAtTheLogoPathAndIsClosed()
    {
        Assert.Equal(354.65, LogoMotion.TileStart.X, 9);
        Assert.Equal(0, LogoMotion.TileStart.Y, 9);
        var last = LogoMotion.TileSegments[^1];
        Assert.Equal(6, last.Length);
        Assert.Equal(LogoMotion.TileStart.X, last[4], 9);
        Assert.Equal(LogoMotion.TileStart.Y, last[5], 9);
    }

    [Fact]
    public void TileIsSymmetricAcrossBothAxes()
    {
        var points = new List<(double X, double Y)> { LogoMotion.TileStart };
        foreach (var segment in LogoMotion.TileSegments)
        {
            for (var i = 0; i < segment.Length; i += 2)
            {
                points.Add((segment[i], segment[i + 1]));
            }
        }

        foreach (var (x, y) in points)
        {
            Assert.Contains(points, p => Math.Abs(p.X - (LogoMotion.Canvas - x)) < 1e-9 && Math.Abs(p.Y - y) < 1e-9);
            Assert.Contains(points, p => Math.Abs(p.X - x) < 1e-9 && Math.Abs(p.Y - (LogoMotion.Canvas - y)) < 1e-9);
        }
    }
}
