using Edgepad.Trust;
using Xunit;

namespace Edgepad.Tests;

public sealed class UserFolderTests : IDisposable
{
    private static readonly byte[] Secret = [0, 1, 2, 250, 13, 10, 255];

    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> logs = [];

    private string Legacy => Path.Combine(dir, "Roaming", "Edgepad");

    private string Current => Path.Combine(dir, "Local", "Edgepad");

    private void Migrate() => UserFolder.Migrate(Legacy, Current, logs.Add);

    [Fact]
    public void FilesMoveByteForByteAndTheEmptyLegacyFolderGoes()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllBytes(Path.Combine(Legacy, "paired-links.txt"), Secret);
        File.WriteAllText(Path.Combine(Legacy, "trusted-phone.txt"), "link:00112233445566778899aabbccddeeff\n");

        Migrate();

        Assert.Equal(Secret, File.ReadAllBytes(Path.Combine(Current, "paired-links.txt")));
        Assert.Equal("link:00112233445566778899aabbccddeeff\n", File.ReadAllText(Path.Combine(Current, "trusted-phone.txt")));
        Assert.False(Directory.Exists(Legacy));
        Assert.DoesNotContain(logs, line => line.Contains("link:", StringComparison.Ordinal));
    }

    [Fact]
    public void AFileAlreadyInTheNewFolderIsKeptAndTheStaleLegacyOneRemoved()
    {
        Directory.CreateDirectory(Legacy);
        Directory.CreateDirectory(Current);
        File.WriteAllText(Path.Combine(Legacy, "pin.txt"), "old");
        File.WriteAllText(Path.Combine(Current, "pin.txt"), "new");

        Migrate();

        Assert.Equal("new", File.ReadAllText(Path.Combine(Current, "pin.txt")));
        Assert.False(File.Exists(Path.Combine(Legacy, "pin.txt")));
    }

    [Fact]
    public void AStaleRoamingCopyAfterAForgetDoesNotComeBack()
    {
        Directory.CreateDirectory(Legacy);
        Assert.True(new TrustStore(Path.Combine(Legacy, "trusted-phone.txt")).Grant("link:00112233445566778899aabbccddeeff"));
        Migrate();

        // A roaming profile syncs the old list back, then the owner forgets the phones.
        Directory.CreateDirectory(Legacy);
        File.Copy(Path.Combine(Current, "trusted-phone.txt"), Path.Combine(Legacy, "trusted-phone.txt"));
        Assert.True(new TrustStore(PathFor("trusted-phone.txt")).Forget());
        Migrate();

        Assert.Equal(Path.Combine(Current, "trusted-phone.txt"), PathFor("trusted-phone.txt"));
        Assert.Empty(new TrustStore(PathFor("trusted-phone.txt")).TrustedPhones);
        Assert.False(File.Exists(Path.Combine(Legacy, "trusted-phone.txt")));
    }

    [Fact]
    public void ARoamingCopyThatCannotBeDeletedIsNeverReadAgain()
    {
        Directory.CreateDirectory(Legacy);
        var legacyFile = Path.Combine(Legacy, "pin-verified.txt");
        File.WriteAllText(legacyFile, "link:00112233445566778899aabbccddeeff\n");

        // Deleting the source fails: on Windows an open handle that does not share delete, on Unix a folder
        // that cannot be written. Reading it still works, so the copy goes through.
        FileStream? held = null;
        if (OperatingSystem.IsWindows())
        {
            held = new FileStream(legacyFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        else
        {
            File.SetUnixFileMode(Legacy, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        try
        {
            Migrate();
        }
        finally
        {
            held?.Dispose();
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Legacy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Assert.True(File.Exists(legacyFile));
        Assert.Contains(logs, line => line.Contains("Could not remove pin-verified.txt", StringComparison.Ordinal));

        // The owner clears the verified list: the Roaming copy must not stand in for it.
        File.Delete(Path.Combine(Current, "pin-verified.txt"));
        Assert.Equal(Path.Combine(Current, "pin-verified.txt"), PathFor("pin-verified.txt"));
        Migrate();
        Assert.False(File.Exists(Path.Combine(Current, "pin-verified.txt")));
    }

    [Fact]
    public void ACrashBetweenCopyAndRenameLeavesTheOldPinReadable()
    {
        Directory.CreateDirectory(Legacy);
        Assert.True(new PinStore(Legacy, iterations: 1).SetPin("123456"));

        // What a crash after the copy began leaves: a partial temporary file, no security.txt in Local yet.
        Directory.CreateDirectory(Current);
        File.WriteAllText(Path.Combine(Current, "security.txt.tmp"), "");

        Assert.Equal(Path.Combine(Legacy, "security.txt"), PathFor("security.txt"));
        Assert.True(Pin().HasPin);

        Migrate();

        Assert.Equal(Path.Combine(Current, "security.txt"), PathFor("security.txt"));
        Assert.False(File.Exists(Path.Combine(Current, "security.txt.tmp")));
        Assert.True(Pin().HasPin);
    }

    [Fact]
    public void AFileThatCannotMoveStaysAndNothingThrows()
    {
        // A folder where the file would go fails the move the same way on every OS.
        Directory.CreateDirectory(Legacy);
        File.WriteAllBytes(Path.Combine(Legacy, "paired-links.txt"), Secret);
        File.WriteAllText(Path.Combine(Legacy, "macros.txt"), "m");
        Directory.CreateDirectory(Path.Combine(Current, "paired-links.txt"));

        Migrate();

        Assert.Equal(Secret, File.ReadAllBytes(Path.Combine(Legacy, "paired-links.txt")));
        Assert.Equal("m", File.ReadAllText(Path.Combine(Current, "macros.txt")));
        Assert.Contains(logs, line => line.Contains("Could not move paired-links.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void ASecondRunAndAMissingLegacyFolderAreNoOps()
    {
        Migrate();
        Assert.False(Directory.Exists(Current));

        Directory.CreateDirectory(Legacy);
        File.WriteAllBytes(Path.Combine(Legacy, "paired-links.txt"), Secret);
        Migrate();
        logs.Clear();
        Migrate();

        Assert.Empty(logs);
        Assert.Equal(Secret, File.ReadAllBytes(Path.Combine(Current, "paired-links.txt")));
    }

    [Fact]
    public void AFailedMoveKeepsThePinAndTrustedPhonesReadable()
    {
        Directory.CreateDirectory(Legacy);
        Assert.True(new PinStore(Legacy, iterations: 1).SetPin("123456"));
        Assert.True(new TrustStore(Path.Combine(Legacy, "trusted-phone.txt")).Grant("link:00112233445566778899aabbccddeeff"));

        // Folders where the files would go fail both moves the same way on every OS.
        Directory.CreateDirectory(Path.Combine(Current, "security.txt"));
        Directory.CreateDirectory(Path.Combine(Current, "trusted-phone.txt"));
        Migrate();

        Assert.Equal(Path.Combine(Legacy, "security.txt"), PathFor("security.txt"));
        Assert.True(Pin().HasPin);
        Assert.Equal(["link:00112233445566778899aabbccddeeff"], new TrustStore(PathFor("trusted-phone.txt")).TrustedPhones);
    }

    [Fact]
    public void AMovedFileIsReadFromTheNewFolderAndNoLegacyCopyIsLeft()
    {
        Directory.CreateDirectory(Legacy);
        Assert.True(new PinStore(Legacy, iterations: 1).SetPin("123456"));

        Migrate();

        Assert.Equal(Path.Combine(Current, "security.txt"), PathFor("security.txt"));
        Assert.False(File.Exists(Path.Combine(Legacy, "security.txt")));
        Assert.True(Pin().HasPin);
    }

    [Fact]
    public void APinMovedWithoutItsVerifiedListIsStillRequired()
    {
        // security.txt reached Local, pin-verified.txt stayed in Roaming: each is read where it is.
        Directory.CreateDirectory(Legacy);
        Assert.True(new PinStore(Legacy, iterations: 1).SetPin("123456"));
        File.WriteAllText(Path.Combine(Legacy, "pin-verified.txt"), "");
        Directory.CreateDirectory(Current);
        File.Move(Path.Combine(Legacy, "security.txt"), Path.Combine(Current, "security.txt"));

        Assert.Equal(Path.Combine(Current, "security.txt"), PathFor("security.txt"));
        Assert.Equal(Path.Combine(Legacy, "pin-verified.txt"), PathFor("pin-verified.txt"));
        Assert.True(Pin().HasPin);
    }

    [Fact]
    public void AVerifiedListMovedWithoutItsPinLeavesThePinRequired()
    {
        // The reverse split: pin-verified.txt reached Local, security.txt stayed in Roaming.
        Directory.CreateDirectory(Legacy);
        Assert.True(new PinStore(Legacy, iterations: 1).SetPin("123456"));
        Directory.CreateDirectory(Current);
        File.WriteAllText(Path.Combine(Current, "pin-verified.txt"), "");

        Assert.Equal(Path.Combine(Legacy, "security.txt"), PathFor("security.txt"));
        Assert.Equal(Path.Combine(Current, "pin-verified.txt"), PathFor("pin-verified.txt"));
        Assert.True(Pin().HasPin);
    }

    private string PathFor(string name) => UserFolder.PathFor(Legacy, Current, logs.Add, name);

    /// <summary>A PinStore opened the way <see cref="PinStore.ForCurrentUser"/> opens it, on the test's folders.</summary>
    private PinStore Pin() => new(
        Current, iterations: 1, securityFile: PathFor("security.txt"), verifiedFile: PathFor("pin-verified.txt"));

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
