using Edgepad.Dashboard;
using Xunit;

namespace Edgepad.Tests;

public sealed class IntegrationSettingsTests : IDisposable
{
    private static readonly string[] Flags =
    [
        IntegrationSettings.Stats,
        IntegrationSettings.Weather,
        IntegrationSettings.Claude,
        IntegrationSettings.Codex,
        IntegrationSettings.GitHub,
        IntegrationSettings.GitHubPrivate,
        IntegrationSettings.CodexTrusted,
    ];

    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-integrations");

    private string FilePath => Path.Combine(dir.FullName, IntegrationSettings.FileName);

    public void Dispose() => dir.Delete(recursive: true);

    private void AssertAllOff(IntegrationSettings settings)
    {
        Assert.All(Flags, key => Assert.False(settings.Flag(key), key));
        Assert.Equal(string.Empty, settings.Text(IntegrationSettings.City));
        Assert.Equal(string.Empty, settings.Text(IntegrationSettings.GitHubLogin));
    }

    [Fact]
    public void ANewInstallHasEverythingOff() => AssertAllOff(new IntegrationSettings(FilePath));

    [Theory]
    [InlineData("\0\0\0zzz")]
    [InlineData("just some words\nand more words")]
    [InlineData("stats\ngithub\n")]
    [InlineData("=1\n==\n=stats=1")]
    [InlineData("stats=yes\nweather=true\nclaude=2\ncodex= 0\ngithubPrivate=maybe\n")]
    [InlineData("bad key=1\nstats!=1\n")]
    [InlineData("")]
    public void GarbageIsAllOff(string content)
    {
        File.WriteAllText(FilePath, content);

        AssertAllOff(new IntegrationSettings(FilePath));
    }

    [Fact]
    public void AFileThatIsTooLargeIsAllOff()
    {
        File.WriteAllText(FilePath, "stats=1\n" + new string('#', 70 * 1024));

        AssertAllOff(new IntegrationSettings(FilePath));
    }

    [Fact]
    public void AnUnreadableFileIsAllOff()
    {
        Directory.CreateDirectory(FilePath);

        AssertAllOff(new IntegrationSettings(FilePath));
    }

    [Fact]
    public void OnlyTheValueOneIsOn()
    {
        File.WriteAllText(FilePath, "stats=1\nweather=0\nclaude=11\ncodex=1 \n");

        var settings = new IntegrationSettings(FilePath);

        Assert.True(settings.Flag(IntegrationSettings.Stats));
        Assert.False(settings.Flag(IntegrationSettings.Weather));
        Assert.False(settings.Flag(IntegrationSettings.Claude));
        Assert.True(settings.Flag(IntegrationSettings.Codex));
    }

    [Fact]
    public void EverythingRoundTrips()
    {
        var settings = new IntegrationSettings(FilePath);

        Assert.True(settings.SetFlag(IntegrationSettings.Stats, true));
        Assert.True(settings.SetFlag(IntegrationSettings.GitHub, true));
        Assert.True(settings.SetFlag(IntegrationSettings.GitHubPrivate, true));
        Assert.True(settings.SetFlag(IntegrationSettings.CodexTrusted, true));
        Assert.True(settings.SetFlag(IntegrationSettings.Weather, false));
        Assert.True(settings.Set(IntegrationSettings.City, "Lisbon"));
        Assert.True(settings.Set(IntegrationSettings.Lat, "38.72"));
        Assert.True(settings.Set(IntegrationSettings.Lon, "-9.14"));
        Assert.True(settings.Set(IntegrationSettings.Place, "Lisbon, Portugal"));
        Assert.True(settings.Set(IntegrationSettings.GitHubLogin, "octo-cat"));
        Assert.True(settings.Set(IntegrationSettings.PluginVersionKey("claude"), "5.0.0"));
        Assert.True(settings.Set(IntegrationSettings.PluginAppKey("codex"), "C:/Program Files/Edgepad/Edgepad.exe"));

        var again = new IntegrationSettings(FilePath);

        Assert.True(again.Flag(IntegrationSettings.Stats));
        Assert.True(again.Flag(IntegrationSettings.GitHub));
        Assert.True(again.Flag(IntegrationSettings.GitHubPrivate));
        Assert.True(again.Flag(IntegrationSettings.CodexTrusted));
        Assert.False(again.Flag(IntegrationSettings.Weather));
        Assert.False(again.Flag(IntegrationSettings.Claude));
        Assert.Equal("Lisbon", again.Text(IntegrationSettings.City));
        Assert.Equal("38.72", again.Text(IntegrationSettings.Lat));
        Assert.Equal("-9.14", again.Text(IntegrationSettings.Lon));
        Assert.Equal("Lisbon, Portugal", again.Text(IntegrationSettings.Place));
        Assert.Equal("octo-cat", again.Text(IntegrationSettings.GitHubLogin));
        Assert.Equal("5.0.0", again.Text(IntegrationSettings.PluginVersionKey("claude")));
        Assert.Equal("C:/Program Files/Edgepad/Edgepad.exe", again.Text(IntegrationSettings.PluginAppKey("codex")));
    }

    [Theory]
    [InlineData("A=B=C")]
    [InlineData("São Paulo")]
    [InlineData("東京")]
    [InlineData("Zürich 🌧")]
    public void AnEqualsSignAndUnicodeSurviveInTheCity(string city)
    {
        new IntegrationSettings(FilePath).Set(IntegrationSettings.City, city);

        Assert.Equal(city, new IntegrationSettings(FilePath).Text(IntegrationSettings.City));
    }

    [Fact]
    public void AValueIsOneLineWithoutControlCharacters()
    {
        var settings = new IntegrationSettings(FilePath);

        settings.Set(IntegrationSettings.City, "  Paris\nstats=1\r\n\tFrance\0  ");

        Assert.Equal("Paris stats=1   France", settings.Text(IntegrationSettings.City));
        Assert.False(new IntegrationSettings(FilePath).Flag(IntegrationSettings.Stats));
        Assert.Single(File.ReadAllLines(FilePath));
    }

    [Fact]
    public void TheCityIsCutAtSixtyCharactersNeverInsideASurrogatePair()
    {
        var settings = new IntegrationSettings(FilePath);

        settings.Set(IntegrationSettings.City, new string('a', 59) + "😀😀");

        Assert.Equal(new string('a', 59) + "😀", settings.Text(IntegrationSettings.City));
        Assert.Equal(60, settings.Text(IntegrationSettings.City).EnumerateRunes().Count());
    }

    [Fact]
    public void AnEmptyValueRemovesTheKey()
    {
        var settings = new IntegrationSettings(FilePath);
        settings.Set(IntegrationSettings.City, "Rome");

        settings.Set(IntegrationSettings.City, "   ");

        Assert.Equal(string.Empty, new IntegrationSettings(FilePath).Text(IntegrationSettings.City));
        Assert.DoesNotContain("city", File.ReadAllText(FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void KeysFromANewerBuildAreKept()
    {
        File.WriteAllText(FilePath, "future.thing=hello wörld\nstats=1\n");
        var settings = new IntegrationSettings(FilePath);

        settings.Set(IntegrationSettings.City, "Oslo");

        var text = File.ReadAllText(FilePath);
        Assert.Contains("future.thing=hello wörld\n", text, StringComparison.Ordinal);
        Assert.Contains("stats=1\n", text, StringComparison.Ordinal);
        Assert.Equal("hello wörld", new IntegrationSettings(FilePath).Text("future.thing"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a=b")]
    [InlineData("a\nb")]
    public void ABadKeyIsRefusedAndNothingIsWritten(string key)
    {
        var settings = new IntegrationSettings(FilePath);

        Assert.False(settings.Set(key, "1"));

        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void AFailedWriteReturnsFalseButTheChangeTakesEffect()
    {
        var blocker = Path.Combine(dir.FullName, "blocker");
        File.WriteAllText(blocker, "a file where a folder should be");
        var settings = new IntegrationSettings(Path.Combine(blocker, IntegrationSettings.FileName));

        Assert.False(settings.SetFlag(IntegrationSettings.Stats, true));

        Assert.True(settings.Flag(IntegrationSettings.Stats));
    }

    [Fact]
    public async Task FourThreadsNeverCorruptTheFile()
    {
        var settings = new IntegrationSettings(FilePath);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                settings.SetFlag(Flags[t], i % 2 == 0);
            }
        }, TestContext.Current.CancellationToken)));

        var again = new IntegrationSettings(FilePath);
        for (var t = 0; t < 4; t++)
        {
            Assert.Equal(settings.Flag(Flags[t]), again.Flag(Flags[t]));
        }
    }
}
