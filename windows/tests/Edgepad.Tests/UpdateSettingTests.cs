using Edgepad.Updates;
using Xunit;

namespace Edgepad.Tests;

public sealed class UpdateSettingTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(dir, "updates.txt");

    [Fact]
    public void NoFileIsOn() => Assert.True(new UpdateSetting(FilePath).Enabled);

    [Fact]
    public void OffIsRememberedAndOnAgain()
    {
        var setting = UpdateSetting.ForCurrentUser(dir);
        Assert.True(setting.Set(false));
        Assert.False(setting.Enabled);
        Assert.False(new UpdateSetting(FilePath).Enabled);

        Assert.True(setting.Set(true));
        Assert.True(new UpdateSetting(FilePath).Enabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
