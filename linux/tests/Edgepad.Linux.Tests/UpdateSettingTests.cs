using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class UpdateSettingTests
{
    [Fact]
    public void ANeverWrittenSettingIsOn()
    {
        var path = Path.Combine(Path.GetTempPath(), $"edgepad-{Guid.NewGuid():N}", "updates.txt");
        Assert.True(new UpdateSetting(path).Enabled);
    }

    [Fact]
    public void OffIsRememberedAndOnAgain()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"edgepad-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "updates.txt");
        try
        {
            var setting = new UpdateSetting(path);
            Assert.True(setting.Set(false));
            Assert.False(setting.Enabled);
            Assert.False(new UpdateSetting(path).Enabled);
            Assert.True(setting.Set(true));
            Assert.True(new UpdateSetting(path).Enabled);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
