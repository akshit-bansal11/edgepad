using Edgepad.Protocol;
using Edgepad.Trust;
using Xunit;

namespace Edgepad.Tests;

public sealed class PinStoreTests : IDisposable
{
    private const string Phone = "(AA:BB:CC:DD:EE:FF)";
    private const string Other = "(11:22:33:44:55:66)";

    private static readonly Pin Right = new("123456");
    private static readonly Pin WrongPin = new("654321");

    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));

    // Milliseconds on the store's clock, moved by hand.
    private long time;

    // One iteration: the real work factor would take most of a second per hash, and the format is the same.
    private PinStore NewStore(int iterations = 1) => new(dir, () => time, iterations);

    private string SecurityFile => Path.Combine(dir, "security.txt");

    private PinStore StoreAtLevel(int level)
    {
        var store = NewStore();
        Assert.True(store.SetPin("123456"));
        Assert.True(store.SetLevel(level));
        return store;
    }

    [Fact]
    public void NoFileIsLevelOneWithNoPin()
    {
        var store = NewStore();

        Assert.Equal(1, store.Level);
        Assert.False(store.HasPin);
        Assert.False(store.Required(Phone));
    }

    [Fact]
    public void TheHashVerifiesAndAWrongPinFails()
    {
        var store = StoreAtLevel(3);

        Assert.Matches(@"^level=3\npin=pbkdf2-sha256\$1\$[A-Za-z0-9+/=]{24}\$[A-Za-z0-9+/=]{44}\n$", File.ReadAllText(SecurityFile));
        Assert.Equal(new PinResult.Wrong(4), store.Check(Phone, WrongPin));
        Assert.Equal(new PinResult.Accepted(), store.Check(Phone, Right));

        // The count comes from the stored hash, not the store, so raising the constant breaks no saved PIN.
        Assert.Equal(new PinResult.Accepted(), NewStore(iterations: 2).Check(Phone, Right));
    }

    [Fact]
    public void TwoSetPinCallsGiveADifferentSaltAndHash()
    {
        var store = NewStore();
        Assert.True(store.SetPin("123456"));
        var first = File.ReadAllText(SecurityFile).Split('$');
        Assert.True(store.SetPin("123456"));
        var second = File.ReadAllText(SecurityFile).Split('$');

        Assert.NotEqual(first[2], second[2]);
        Assert.NotEqual(first[3], second[3]);
    }

    [Fact]
    public void FiveFailuresLockFor30SecondsAndEachLaterBatchDoublesUpToAnHour()
    {
        var store = StoreAtLevel(3);
        for (var left = 4; left > 0; left--)
        {
            Assert.Equal(new PinResult.Wrong(left), store.Check(Phone, WrongPin));
        }

        Assert.Equal(new PinResult.Locked(30), store.Check(Phone, WrongPin));

        // Locked out means the answer is not looked at, the right one included; the key ignores case, and another
        // phone is not affected.
        Assert.Equal(new PinResult.Locked(30), store.Check(Phone, Right));
        Assert.Equal(TimeSpan.FromSeconds(30), store.LockedFor("(aa:bb:cc:dd:ee:ff)"));
        Assert.Equal(0, store.AttemptsLeft(Phone));
        Assert.Null(store.LockedFor(Other));

        foreach (var seconds in new[] { 60, 120, 240, 480, 960, 1920, 3600, 3600 })
        {
            time += 3_600_000;
            Assert.Equal(5, store.AttemptsLeft(Phone));
            for (var i = 0; i < 4; i++)
            {
                store.Check(Phone, WrongPin);
            }

            Assert.Equal(new PinResult.Locked(seconds), store.Check(Phone, WrongPin));
        }
    }

    [Fact]
    public void TheLockExpiresOnTheClock()
    {
        var store = StoreAtLevel(3);
        for (var i = 0; i < 5; i++)
        {
            store.Check(Phone, WrongPin);
        }

        time += 29_500;
        Assert.Equal(new PinResult.Locked(1), store.Check(Phone, Right));

        time += 500;
        Assert.Null(store.LockedFor(Phone));
        Assert.Equal(new PinResult.Accepted(), store.Check(Phone, Right));
    }

    [Fact]
    public void ASuccessResetsTheFailures()
    {
        var store = StoreAtLevel(3);
        for (var i = 0; i < 4; i++)
        {
            store.Check(Phone, WrongPin);
        }

        Assert.Equal(new PinResult.Accepted(), store.Check(Phone, Right));
        Assert.Equal(5, store.AttemptsLeft(Phone));
        for (var i = 0; i < 4; i++)
        {
            store.Check(Phone, WrongPin);
        }

        Assert.Null(store.LockedFor(Phone));
        Assert.Equal(1, store.AttemptsLeft(Phone));
    }

    [Fact]
    public void LevelTwoAsksOnceAndLevelThreeEveryTime()
    {
        var two = StoreAtLevel(2);
        Assert.True(two.Required(Phone));
        Assert.Equal(new PinResult.Accepted(), two.Check(Phone, Right));
        Assert.False(two.Required("(aa:bb:cc:dd:ee:ff)"));
        Assert.False(NewStore().Required(Phone));
        Assert.True(two.Required(Other));

        var three = StoreAtLevel(3);
        Assert.Equal(new PinResult.Accepted(), three.Check(Phone, Right));
        Assert.True(three.Required(Phone));
    }

    [Fact]
    public void SetPinAndSetLevelClearTheVerifiedList()
    {
        var store = StoreAtLevel(2);
        store.Check(Phone, Right);
        Assert.True(store.SetPin("111111"));
        Assert.True(store.Required(Phone));

        store.Check(Phone, new Pin("111111"));
        Assert.False(store.Required(Phone));
        Assert.True(store.SetLevel(2));
        Assert.True(store.Required(Phone));
    }

    [Fact]
    public void LevelsTwoAndThreeAreRefusedWithoutAPin()
    {
        var store = NewStore();

        Assert.False(store.SetLevel(2));
        Assert.False(store.SetLevel(3));
        Assert.False(store.SetLevel(4));
        Assert.Equal(1, store.Level);
        Assert.Equal(new PinResult.Unavailable(), store.Check(Phone, Right));
    }

    [Fact]
    public void AFileThatCannotBeReadFailsClosed()
    {
        // Held open with no sharing, which is how a locked file looks on Windows. A phone verified before is asked
        // again and cannot be answered: an unreadable PIN is never "no PIN".
        var store = StoreAtLevel(2);
        store.Check(Phone, Right);

        using (new FileStream(SecurityFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.True(store.Required(Phone));
            Assert.Equal(new PinResult.Unavailable(), store.Check(Phone, Right));
            Assert.Equal(3, store.Level);
            Assert.True(store.HasPin);
            Assert.False(store.SetPin("111111"));
            Assert.False(store.SetLevel(1));
        }

        Assert.False(store.Required(Phone));
    }

    [Theory]
    [InlineData("level=2\n")]
    [InlineData("level=1\npin=nonsense\n")]
    [InlineData("level=9\n")]
    [InlineData("garbled")]
    public void AFileEdgepadWouldNotWriteFailsClosed(string text)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(SecurityFile, text);
        var store = NewStore();

        Assert.True(store.Required(Phone));
        Assert.Equal(new PinResult.Unavailable(), store.Check(Phone, Right));
        Assert.True(store.Reset());
        Assert.False(store.Required(Phone));
    }

    [Fact]
    public void ResetForgetsThePinAndTheVerifiedPhones()
    {
        var store = StoreAtLevel(2);
        store.Check(Phone, Right);

        Assert.True(store.Reset());
        Assert.Equal(1, store.Level);
        Assert.False(store.HasPin);
        Assert.True(store.SetPin("123456"));
        Assert.True(store.SetLevel(2));
        Assert.True(store.Required(Phone));
    }

    [Fact]
    public void TheDigitsAppearInNoExceptionAndNoFile()
    {
        var store = NewStore();

        Assert.DoesNotContain("12345", Assert.Throws<ArgumentException>(() => store.SetPin("12345")).Message, StringComparison.Ordinal);
        Assert.DoesNotContain("1234567", Assert.Throws<ArgumentException>(() => store.SetPin("1234567")).Message, StringComparison.Ordinal);
        Assert.DoesNotContain("12a456", Assert.Throws<ArgumentException>(() => store.SetPin("12a456")).Message, StringComparison.Ordinal);

        Assert.True(store.SetPin("123456"));
        Assert.DoesNotContain("123456", File.ReadAllText(SecurityFile), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
