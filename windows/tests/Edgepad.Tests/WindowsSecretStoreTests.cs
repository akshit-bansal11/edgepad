using Edgepad.Dashboard;
using Xunit;

namespace Edgepad.Tests;

public sealed class WindowsSecretStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "edgepad-secrets-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ASecretRoundTripsAndIsNotStoredInTheClear()
    {
        var store = new WindowsSecretStore(folder);

        Assert.True(store.Set("github.token", "gho_ExampleToken123"));
        Assert.Equal("gho_ExampleToken123", store.Get("github.token"));

        var onDisk = File.ReadAllBytes(Path.Combine(folder, "github.token.bin"));
        Assert.DoesNotContain("gho_ExampleToken123", System.Text.Encoding.UTF8.GetString(onDisk), StringComparison.Ordinal);
    }

    [Fact]
    public void SetReplacesTheEarlierSecret()
    {
        var store = new WindowsSecretStore(folder);

        Assert.True(store.Set("a", "first"));
        Assert.True(store.Set("a", "second"));

        Assert.Equal("second", store.Get("a"));
        Assert.False(File.Exists(Path.Combine(folder, "a.bin.tmp")));
    }

    [Fact]
    public void AMissingSecretIsNull() => Assert.Null(new WindowsSecretStore(folder).Get("github.token"));

    [Fact]
    public void ACorruptedFileIsNullAndDoesNotThrow()
    {
        var store = new WindowsSecretStore(folder);
        Assert.True(store.Set("github.token", "gho_x"));
        var path = Path.Combine(folder, "github.token.bin");
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        Assert.Null(store.Get("github.token"));

        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Null(store.Get("github.token"));

        File.WriteAllBytes(path, []);
        Assert.Null(store.Get("github.token"));
    }

    [Fact]
    public void ABlobFromAnotherEntropyIsRefused()
    {
        var foreign = System.Security.Cryptography.RandomNumberGenerator.GetBytes(64);

        Assert.Null(WindowsSecretStore.Unprotect(foreign));
    }

    [Fact]
    public void DeleteRemovesTheFileAndAMissingOneIsFine()
    {
        var store = new WindowsSecretStore(folder);
        Assert.True(store.Set("github.token", "gho_x"));

        store.Delete("github.token");

        Assert.False(File.Exists(Path.Combine(folder, "github.token.bin")));
        Assert.Null(store.Get("github.token"));
        store.Delete("github.token");
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("..\\evil")]
    [InlineData("../evil")]
    [InlineData("C:evil")]
    [InlineData("a b")]
    [InlineData("name\0")]
    public void ABadNameHasNoFile(string name)
    {
        var store = new WindowsSecretStore(folder);

        Assert.False(WindowsSecretStore.IsName(name));
        Assert.False(store.Set(name, "x"));
        Assert.Null(store.Get(name));
        store.Delete(name);
        Assert.False(Directory.Exists(folder));
    }
}
