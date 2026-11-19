using Edgepad.Dashboard;
using Xunit;

namespace Edgepad.Tests;

public sealed class LinuxSecretStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Directory.CreateTempSubdirectory("edgepad-secrets").FullName, "secrets");

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);

    [Fact]
    public void WithoutAServiceTheFileRoundTrips()
    {
        var store = new LinuxSecretStore(new FakeService { Present = false }, folder);
        Assert.True(store.Set("github.token", "gho_abc123"));
        Assert.False(store.IsEncrypted);
        Assert.Equal("gho_abc123", store.Get("github.token"));
        store.Delete("github.token");
        Assert.Null(store.Get("github.token"));
        Assert.False(File.Exists(Path.Combine(folder, "github.token")));
    }

    [Fact]
    public void TheFileAndItsFolderAreTheUsersOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(folder);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var store = new LinuxSecretStore(new FakeService { Present = false }, folder);
        Assert.True(store.Set("github.token", "secret"));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(folder));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(folder, "github.token")));
    }

    [Fact]
    public void AFileOthersCanReadIsIgnored()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new LinuxSecretStore(new FakeService { Present = false }, folder);
        Assert.True(store.Set("github.token", "secret"));
        var file = Path.Combine(folder, "github.token");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        Assert.Null(store.Get("github.token"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("edgepad-secret 1\n")]
    [InlineData("edgepad-secret 1\n***not base64***\n")]
    [InlineData("edgepad-secret 9\nc2VjcmV0\n")]
    [InlineData("edgepad-secret 1\n/w==\n")] // one byte, 0xFF: not UTF-8
    public void ACorruptedFileReadsAsNoSecret(string text)
    {
        var store = new LinuxSecretStore(new FakeService { Present = false }, folder);
        Assert.True(store.Set("github.token", "secret"));
        var file = Path.Combine(folder, "github.token");
        File.WriteAllText(file, text);
        Assert.Null(store.Get("github.token"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData(".hidden")]
    [InlineData("a/b")]
    [InlineData("")]
    public void ANameThatCouldLeaveTheFolderIsRefused(string name)
    {
        var service = new FakeService();
        var store = new LinuxSecretStore(service, folder);
        Assert.False(store.Set(name, "x"));
        Assert.Null(store.Get(name));
        Assert.Empty(service.Items);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void TheServiceIsUsedWhenItIsThere()
    {
        var service = new FakeService();
        var store = new LinuxSecretStore(service, folder);
        Assert.True(store.Set("github.token", "gho_é"));
        Assert.True(store.IsEncrypted);
        Assert.False(Directory.Exists(folder));
        var item = Assert.Single(service.Items);
        Assert.Equal("edgepad", item.Value.Attributes["application"]);
        Assert.Equal("github.token", item.Value.Attributes["name"]);
        Assert.Equal("gho_é", store.Get("github.token"));
        store.Delete("github.token");
        Assert.Empty(service.Items);
        Assert.Null(store.Get("github.token"));
    }

    [Fact]
    public void ALockedCollectionFallsBackToTheFile()
    {
        var service = new FakeService { Locked = true };
        var store = new LinuxSecretStore(service, folder);
        Assert.True(store.Set("github.token", "secret"));
        Assert.False(store.IsEncrypted);
        Assert.Empty(service.Items);
        Assert.Equal("secret", store.Get("github.token"));
    }

    [Fact]
    public void SavingToTheServiceRemovesAnOlderFileCopy()
    {
        var service = new FakeService { Present = false };
        var store = new LinuxSecretStore(service, folder);
        Assert.True(store.Set("github.token", "old"));
        service.Present = true;
        Assert.True(store.Set("github.token", "new"));
        Assert.False(File.Exists(Path.Combine(folder, "github.token")));
        Assert.Equal("new", store.Get("github.token"));
    }

    private sealed class FakeService : ISecretService
    {
        private int next;

        public bool Present { get; set; } = true;

        public bool Locked { get; init; }

        public Dictionary<string, (Dictionary<string, string> Attributes, byte[] Secret)> Items { get; } = [];

        public string? OpenSession() => Present ? "/org/freedesktop/secrets/session/s1" : null;

        public string? DefaultCollection() => Present ? "/org/freedesktop/secrets/collection/login" : null;

        public (string[] Unlocked, string[] Locked)? Search(IReadOnlyDictionary<string, string> attributes)
        {
            if (!Present)
            {
                return null;
            }

            var found = Items.Where(i => attributes.All(a => i.Value.Attributes.GetValueOrDefault(a.Key) == a.Value)).Select(i => i.Key).ToArray();
            return Locked ? (Array.Empty<string>(), found) : (found, Array.Empty<string>());
        }

        public byte[]? GetSecret(string item, string session) => Present && Items.TryGetValue(item, out var i) ? i.Secret : null;

        public bool Create(string collection, string session, string label, IReadOnlyDictionary<string, string> attributes, byte[] secret)
        {
            if (!Present || Locked)
            {
                return false;
            }

            var existing = Search(attributes)?.Unlocked.FirstOrDefault();
            Items[existing ?? $"/item/{next++}"] = (new Dictionary<string, string>(attributes), secret);
            return true;
        }

        public bool Delete(string item) => Present && Items.Remove(item);
    }
}
