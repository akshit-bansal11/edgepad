using Edgepad.Macros;
using Xunit;

namespace Edgepad.Tests;

/// <summary>The cache both laptop apps put their macro icons through: it renders once per file version and size.</summary>
public sealed class IconCacheTests : IDisposable
{
    private readonly string file = Path.Combine(Path.GetTempPath(), "edgepad-icon-" + Guid.NewGuid().ToString("N") + ".png");

    public IconCacheTests() => File.WriteAllBytes(file, [1, 2, 3]);

    public void Dispose() => File.Delete(file);

    [Fact]
    public void ARenderIsKeptForTheSameFileAndSize()
    {
        var cache = new IconCache();
        var renders = 0;
        Func<string, int, byte[]?> render = (_, size) =>
        {
            renders++;
            return [(byte)size];
        };

        Assert.Equal([64], cache.Get(file, 64, render));
        Assert.Equal([64], cache.Get(file, 64, render));
        Assert.Equal(1, renders);

        Assert.Equal([128], cache.Get(file, 128, render));
        Assert.Equal(2, renders);
    }

    [Fact]
    public void ANullRenderIsKeptToo()
    {
        var cache = new IconCache();
        var renders = 0;
        Func<string, int, byte[]?> render = (_, _) =>
        {
            renders++;
            return null;
        };

        Assert.Null(cache.Get(file, 64, render));
        Assert.Null(cache.Get(file, 64, render));
        Assert.Equal(1, renders);
    }

    [Fact]
    public void ARewrittenFileIsRenderedAgain()
    {
        var cache = new IconCache();
        var renders = 0;
        Func<string, int, byte[]?> render = (_, _) =>
        {
            renders++;
            return [(byte)renders];
        };

        Assert.Equal([1], cache.Get(file, 64, render));
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddMinutes(1));
        Assert.Equal([2], cache.Get(file, 64, render));
    }
}
