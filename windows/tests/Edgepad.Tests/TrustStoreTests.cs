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

    [Fact]
    public void ABlankFileMeansNothingIsTrustedYet()
    {
        // What Forget leaves behind is no file at all, but a file emptied by hand says the same thing and
        // must not become a laptop that refuses every phone with no way back.
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "trusted-phone.txt"), "   \r\n");

        var store = NewStore();

        Assert.Null(store.Trusted);
        Assert.True(store.Admit("(11:22:33:44:55:66)"));
    }

    [Fact]
    public void AFileThatCannotBeReadRefusesRatherThanReArmingTrust()
    {
        // The security half of guarding the read. A file that is there and unreadable is not the same as
        // no file: treating it as one would re-arm trust on first use and hand this laptop to whichever
        // phone connected while it was locked. Holding it open with no sharing is how a locked file
        // actually looks on Windows, which is the only platform this app has.
        var store = NewStore();
        store.Admit("(AA:BB:CC:DD:EE:FF)");
        using var held = new FileStream(
            Path.Combine(dir, "trusted-phone.txt"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.False(store.Admit("(AA:BB:CC:DD:EE:FF)"));
        Assert.False(store.Admit("(11:22:33:44:55:66)"));
        Assert.Null(store.Trusted);
    }

    [Fact]
    public void APhoneThatCouldNotBeSavedIsStillTheOnlyOneTrusted()
    {
        // A file where the folder should be: the read finds nothing, and the write cannot create the folder.
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "blocked"), "");
        var store = new TrustStore(Path.Combine(dir, "blocked", "trusted-phone.txt"));

        Assert.True(store.Admit("(AA:BB:CC:DD:EE:FF)"));
        Assert.False(store.Admit("(11:22:33:44:55:66)"));
        Assert.True(store.Refuses("(11:22:33:44:55:66)"));
        Assert.Equal("(AA:BB:CC:DD:EE:FF)", store.Trusted);
    }

    [Fact]
    public void RefusesOnlyWhatTheSessionWouldRefuse()
    {
        var store = NewStore();
        Assert.False(store.Refuses("(11:22:33:44:55:66)"));

        store.Admit("(AA:BB:CC:DD:EE:FF)");
        Assert.False(store.Refuses("(aa:bb:cc:dd:ee:ff)"));
        Assert.True(store.Refuses("(11:22:33:44:55:66)"));

        using var held = new FileStream(
            Path.Combine(dir, "trusted-phone.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.True(store.Refuses("(AA:BB:CC:DD:EE:FF)"));
    }

    [Fact]
    public void ForgettingWhatIsNotThereIsNotAnError()
    {
        // Forget runs from a tray menu click, where an unhandled exception ends the process.
        NewStore().Forget();
        NewStore().Forget();

        Assert.Null(NewStore().Trusted);
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
