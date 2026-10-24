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
        Assert.True(NewStore().Admit("(AA:BB:CC:DD:EE:FF)", Yes));
        Assert.Equal(["(AA:BB:CC:DD:EE:FF)"], NewStore().TrustedPhones);
    }

    [Fact]
    public void ASecondPhoneIsAskedAboutAndThenBothAreAdmittedUnasked()
    {
        var store = NewStore();
        store.Admit("(AA:BB:CC:DD:EE:FF)", Yes);

        // Another phone being trusted is no longer a reason to refuse one: it is put to the owner like the first.
        Assert.False(store.Refuses("(11:22:33:44:55:66)"));
        Assert.True(store.Admit("(11:22:33:44:55:66)", Yes));

        var again = NewStore();
        Assert.True(again.Admit("(aa:bb:cc:dd:ee:ff)", Unasked));
        Assert.True(again.Admit("(11:22:33:44:55:66)", Unasked));
        Assert.Equal(["(AA:BB:CC:DD:EE:FF)", "(11:22:33:44:55:66)"], again.TrustedPhones);
    }

    [Fact]
    public void AOneLineFileFromBeforeSeveralPhonesIsHonoured()
    {
        // What every earlier version wrote: one address, no line break. No migration; it is a list of one.
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "trusted-phone.txt"), "(AA:BB:CC:DD:EE:FF)");

        var store = NewStore();

        Assert.True(store.Admit("(AA:BB:CC:DD:EE:FF)", Unasked));
        Assert.Equal(["(AA:BB:CC:DD:EE:FF)"], store.TrustedPhones);
    }

    [Fact]
    public void APhoneIsNeverWrittenDownTwice()
    {
        // A second store on the same file trusts the phone while the first is still asking about it; the first
        // one's yes then finds it already there. A list edited by hand with the same phone twice reads as one.
        var file = Path.Combine(dir, "trusted-phone.txt");
        var store = NewStore();

        Assert.True(store.Admit("(AA:BB:CC:DD:EE:FF)", () => NewStore().Admit("(aa:bb:cc:dd:ee:ff)", Yes)));
        Assert.Single(File.ReadAllLines(file));

        File.WriteAllText(file, "(AA:BB:CC:DD:EE:FF)\r\n(aa:bb:cc:dd:ee:ff)\n\n  (11:22:33:44:55:66)  \n");
        Assert.Equal(["(AA:BB:CC:DD:EE:FF)", "(11:22:33:44:55:66)"], NewStore().TrustedPhones);
    }

    [Fact]
    public void ForgetClearsEveryPhone()
    {
        var store = NewStore();
        store.Admit("(AA:BB:CC:DD:EE:FF)", Yes);
        store.Admit("(11:22:33:44:55:66)", Yes);

        Assert.True(store.Forget());

        Assert.Empty(store.TrustedPhones);
        Assert.False(store.CanForget);
        var asked = 0;
        Assert.True(store.Admit("(AA:BB:CC:DD:EE:FF)", () => ++asked > 0));
        Assert.True(store.Admit("(11:22:33:44:55:66)", () => ++asked > 0));
        Assert.Equal(2, asked);
    }

    [Fact]
    public void AForgetWhileTheOwnerIsBeingAskedMakesTheirYesSaveNothing()
    {
        // The owner chose Forget after the question appeared, so Forget is the later word.
        var store = NewStore();
        store.Admit("(AA:BB:CC:DD:EE:FF)", Yes);

        Assert.False(store.Admit("(11:22:33:44:55:66)", () =>
        {
            store.Forget();
            return true;
        }));

        Assert.Empty(store.TrustedPhones);
        Assert.Empty(NewStore().TrustedPhones);
        Assert.False(store.CanForget);
    }

    [Fact]
    public void ABlankFileMeansNothingIsTrustedYet()
    {
        // What Forget leaves behind is no file at all, but a file emptied by hand says the same thing and
        // must not become a laptop that refuses every phone with no way back.
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "trusted-phone.txt"), "   \r\n");

        var store = NewStore();

        Assert.Empty(store.TrustedPhones);
        Assert.True(store.Admit("(11:22:33:44:55:66)", Yes));
    }

    [Fact]
    public void AFileThatCannotBeReadRefusesRatherThanReArmingTrust()
    {
        // The security half of guarding the read. A file that is there and unreadable is not the same as
        // no file: treating it as one would put every phone to the owner again, and a yes would write a list
        // of one over the phones the read could not see. Holding it open with no sharing is how a locked file
        // actually looks on Windows, which is the only platform this app has.
        var store = NewStore();
        store.Admit("(AA:BB:CC:DD:EE:FF)", Yes);
        using var held = new FileStream(
            Path.Combine(dir, "trusted-phone.txt"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.False(store.Admit("(AA:BB:CC:DD:EE:FF)", Unasked));
        Assert.False(store.Admit("(11:22:33:44:55:66)", Unasked));
        Assert.Empty(store.TrustedPhones);
    }

    [Fact]
    public void APhoneThatCouldNotBeSavedIsStillTrustedForThisRun()
    {
        // A file where the folder should be: the read finds nothing, and the write cannot create the folder.
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "blocked"), "");
        var store = new TrustStore(Path.Combine(dir, "blocked", "trusted-phone.txt"));

        Assert.True(store.Admit("(AA:BB:CC:DD:EE:FF)", Yes));
        Assert.True(store.Admit("(aa:bb:cc:dd:ee:ff)", Unasked));
        Assert.True(store.CanForget);
        Assert.Equal(["(AA:BB:CC:DD:EE:FF)"], store.TrustedPhones);

        // Only the owner's yes widens it: another phone is still asked, and a no keeps it out.
        Assert.False(store.Admit("(11:22:33:44:55:66)", No));
    }

    [Fact]
    public void RefusesOnlyWhatTheSessionWouldRefuse()
    {
        var store = NewStore();
        Assert.False(store.Refuses("(11:22:33:44:55:66)"));

        store.Admit("(AA:BB:CC:DD:EE:FF)", Yes);
        Assert.False(store.Refuses("(aa:bb:cc:dd:ee:ff)"));

        // A phone not trusted yet is asked about, not refused before it gets that far.
        Assert.False(store.Refuses("(11:22:33:44:55:66)"));

        using var held = new FileStream(
            Path.Combine(dir, "trusted-phone.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.True(store.Refuses("(AA:BB:CC:DD:EE:FF)"));
        Assert.True(store.Refuses("(11:22:33:44:55:66)"));
    }

    [Fact]
    public void TheOwnerIsAskedOnlyAboutAPhoneNotTrustedYet()
    {
        var store = NewStore();
        Assert.True(store.Admit("(AA:BB:CC:DD:EE:FF)", Yes));

        // The trusted phone comes straight in without a question; any other is asked about.
        Assert.True(store.Admit("(aa:bb:cc:dd:ee:ff)", Unasked));
        var asked = false;
        Assert.False(store.Admit("(11:22:33:44:55:66)", () =>
        {
            asked = true;
            return false;
        }));
        Assert.True(asked);
    }

    [Fact]
    public void APhoneTheOwnerDoesNotTrustIsRefusedAndNotRemembered()
    {
        var store = NewStore();

        Assert.False(store.Admit("(AA:BB:CC:DD:EE:FF)", No));
        Assert.Empty(store.TrustedPhones);

        // Nothing was written down, so the next phone is asked about in its turn.
        Assert.True(store.Admit("(11:22:33:44:55:66)", Yes));
        Assert.Equal(["(11:22:33:44:55:66)"], store.TrustedPhones);
    }

    [Fact]
    public void ASecondPhoneWhileTheOwnerIsBeingAskedIsRefusedUnasked()
    {
        // The second phone arrives while the first question is still open. It is refused without a second
        // question, which the owner could otherwise answer believing it was about the first phone.
        var store = NewStore();
        var second = true;

        Assert.True(store.Admit("(AA:BB:CC:DD:EE:FF)", () =>
        {
            second = store.Admit("(11:22:33:44:55:66)", Unasked);
            return true;
        }));

        Assert.False(second);
        Assert.Equal(["(AA:BB:CC:DD:EE:FF)"], store.TrustedPhones);
    }

    [Fact]
    public void EveryPhoneIsRefusedWhileTheOwnerIsBeingAsked()
    {
        var store = NewStore();
        var refused = false;

        store.Admit("(AA:BB:CC:DD:EE:FF)", () =>
        {
            refused = store.Refuses("(11:22:33:44:55:66)");
            return true;
        });

        Assert.True(refused);
        Assert.False(store.Refuses("(AA:BB:CC:DD:EE:FF)"));
    }

    [Fact]
    public void APhoneTheOwnerDeclinedIsNotAskedAgainUntilTheQuietPeriodEnds()
    {
        long time = 0;
        var store = new TrustStore(Path.Combine(dir, "trusted-phone.txt"), () => time);
        var asked = 0;

        Assert.False(store.Admit("(AA:BB:CC:DD:EE:FF)", () =>
        {
            asked++;
            return false;
        }));

        time = 29_999;
        Assert.False(store.Admit("(aa:bb:cc:dd:ee:ff)", Unasked));
        Assert.True(store.Refuses("(AA:BB:CC:DD:EE:FF)"));

        time = 30_000;
        Assert.False(store.Refuses("(AA:BB:CC:DD:EE:FF)"));
        Assert.True(store.Admit("(AA:BB:CC:DD:EE:FF)", () =>
        {
            asked++;
            return true;
        }));
        Assert.Equal(2, asked);
    }

    [Fact]
    public void ADeclineDoesNotStopAnotherPhoneBeingAsked()
    {
        long time = 0;
        var store = new TrustStore(Path.Combine(dir, "trusted-phone.txt"), () => time);
        store.Admit("(AA:BB:CC:DD:EE:FF)", No);

        Assert.False(store.Refuses("(11:22:33:44:55:66)"));
        Assert.True(store.Admit("(11:22:33:44:55:66)", Yes));
    }

    [Fact]
    public void ForgetEndsTheQuietPeriod()
    {
        long time = 0;
        var store = new TrustStore(Path.Combine(dir, "trusted-phone.txt"), () => time);
        store.Admit("(AA:BB:CC:DD:EE:FF)", No);

        store.Forget();

        Assert.True(store.Admit("(AA:BB:CC:DD:EE:FF)", Yes));
    }

    [Fact]
    public void ForgetIsOfferedOnlyWhenThereIsSomethingToClear()
    {
        var store = NewStore();
        Assert.False(store.CanForget);

        store.Admit("(AA:BB:CC:DD:EE:FF)", Yes);
        Assert.True(store.CanForget);

        // Unreadable refuses every phone, and Forget is the way out, so it must be offered.
        using var held = new FileStream(
            Path.Combine(dir, "trusted-phone.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.True(store.CanForget);
    }

    [Fact]
    public void ForgettingWhatIsNotThereIsNotAnError()
    {
        // Forget runs from a tray menu click, where an unhandled exception ends the process.
        NewStore().Forget();
        NewStore().Forget();

        Assert.Empty(NewStore().TrustedPhones);
    }

    [Fact]
    public void AForgetThatCannotDeleteTheFileRefusesEveryPhoneUntilOneCan()
    {
        // The phone is still named in the file, so a failed delete must not leave it trusted while the owner
        // believes it is gone.
        var store = NewStore();
        store.Admit("(AA:BB:CC:DD:EE:FF)", Yes);
        var held = new FileStream(
            Path.Combine(dir, "trusted-phone.txt"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.False(store.Forget());
        held.Dispose();

        // Readable again and still naming the phone, yet refused.
        Assert.True(store.Refuses("(AA:BB:CC:DD:EE:FF)"));
        Assert.False(store.Admit("(AA:BB:CC:DD:EE:FF)", Unasked));
        Assert.Empty(store.TrustedPhones);
        Assert.True(store.CanForget);

        Assert.True(store.Forget());
        Assert.False(store.Refuses("(AA:BB:CC:DD:EE:FF)"));
        Assert.True(store.Admit("(AA:BB:CC:DD:EE:FF)", Yes));
    }

    [Fact]
    public void AFileThatCannotBeDeletedIsEmptiedInstead()
    {
        // FileShare.ReadWrite without Delete: the delete hits a sharing violation while a write is still let
        // through, which is a file that can be emptied but not removed.
        var store = NewStore();
        store.Admit("(AA:BB:CC:DD:EE:FF)", Yes);
        var held = new FileStream(
            Path.Combine(dir, "trusted-phone.txt"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        Assert.True(store.Forget());
        held.Dispose();

        Assert.Equal("", File.ReadAllText(Path.Combine(dir, "trusted-phone.txt")));
        Assert.Empty(store.TrustedPhones);
        Assert.False(store.CanForget);
        Assert.True(store.Admit("(11:22:33:44:55:66)", Yes));
    }

    [Fact]
    public void OnUnixTheTrustFileIsPrivateToItsOwner()
    {
        // The Windows per-user folder already is; there is nothing to assert there.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        NewStore().Admit("(AA:BB:CC:DD:EE:FF)", Yes);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(dir, "trusted-phone.txt")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir));
    }

    private static bool Yes() => true;

    private static bool No() => false;

    private static bool Unasked() => throw new InvalidOperationException("the owner was asked when nothing needed asking");

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
