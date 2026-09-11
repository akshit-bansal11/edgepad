using Edgepad.Trust;
using Xunit;

namespace Edgepad.Tests;

public sealed class TrustStoreTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));

    private TrustStore NewStore() => new(Path.Combine(dir, "trusted-phone.txt"));

    [Fact]
    public void TheFirstPhoneIsTrustedAndRemembered()
    {
        Assert.True(NewStore().Admit("(AA:BB:CC:DD:EE:FF)"));
        Assert.Equal("(AA:BB:CC:DD:EE:FF)", NewStore().Trusted);
    }

    [Fact]
    public void OnlyTheTrustedPhoneIsAdmittedAfterThat()
    {
        var store = NewStore();
        store.Admit("(AA:BB:CC:DD:EE:FF)");

        Assert.True(store.Admit("(aa:bb:cc:dd:ee:ff)"));
        Assert.False(store.Admit("(11:22:33:44:55:66)"));
    }

    [Fact]
    public void ForgetLetsTheNextPhoneBecomeTrusted()
    {
        var store = NewStore();
        store.Admit("(AA:BB:CC:DD:EE:FF)");

        store.Forget();

        Assert.Null(store.Trusted);
        Assert.True(store.Admit("(11:22:33:44:55:66)"));
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
