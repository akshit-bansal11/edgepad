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
    public void AFileAlreadyInTheNewFolderIsKeptAndTheLegacyOneLeft()
    {
        Directory.CreateDirectory(Legacy);
        Directory.CreateDirectory(Current);
        File.WriteAllText(Path.Combine(Legacy, "pin.txt"), "old");
        File.WriteAllText(Path.Combine(Current, "pin.txt"), "new");

        Migrate();

        Assert.Equal("new", File.ReadAllText(Path.Combine(Current, "pin.txt")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Legacy, "pin.txt")));
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

        var pinFolder = UserFolder.FolderFor(Legacy, Current, logs.Add, "security.txt", "pin-verified.txt");
        var trustFolder = UserFolder.FolderFor(Legacy, Current, logs.Add, "trusted-phone.txt");
        Assert.Equal(Legacy, pinFolder);
        Assert.True(new PinStore(pinFolder, iterations: 1).HasPin);
        Assert.Equal(
            ["link:00112233445566778899aabbccddeeff"],
            new TrustStore(Path.Combine(trustFolder, "trusted-phone.txt")).TrustedPhones);
    }

    [Fact]
    public void AMovedFileIsReadFromTheNewFolderAndNoLegacyCopyIsLeft()
    {
        Directory.CreateDirectory(Legacy);
        Assert.True(new PinStore(Legacy, iterations: 1).SetPin("123456"));

        Migrate();

        var folder = UserFolder.FolderFor(Legacy, Current, logs.Add, "security.txt", "pin-verified.txt");
        Assert.Equal(Current, folder);
        Assert.False(File.Exists(Path.Combine(Legacy, "security.txt")));
        Assert.True(new PinStore(folder, iterations: 1).HasPin);
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
