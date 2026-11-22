using Edgepad.Blob;
using Edgepad.Dashboard;
using Edgepad.Net;
using Edgepad.Protocol;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class DevelopersWindowTests : IDisposable
{
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-dev-window-");

    public void Dispose() => dir.Delete(recursive: true);

    private IntegrationSettings Settings(params string[] on)
    {
        var settings = new IntegrationSettings(Path.Combine(dir.FullName, IntegrationSettings.FileName));
        foreach (var key in on)
        {
            Assert.True(settings.SetFlag(key, true));
        }

        return settings;
    }

    private static IntegrationHost Host(IntegrationSettings settings, DashboardParts parts) =>
        new(_ => { }, new BlobSender(_ => { }, LinkKind.Bluetooth, () => 0, CancellationToken.None, () => long.MaxValue, _ => true), settings, parts);

    private sealed class MemorySecrets : ISecretStore
    {
        private readonly Dictionary<string, string> values = [];

        public string? Get(string name) => values.GetValueOrDefault(name);

        public bool Set(string name, string value)
        {
            values[name] = value;
            return true;
        }

        public void Delete(string name) => values.Remove(name);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("gemini")]
    [InlineData("opencode")]
    [InlineData("github")]
    [InlineData("gitlab")]
    [InlineData("gitea")]
    [InlineData("bitbucket")]
    [InlineData("gitee")]
    public void EveryDeveloperKeyNeedsDeveloperMode(string key)
    {
        var settings = Settings(key);
        Assert.False(settings.Active(key));
        Assert.True(settings.SetFlag(IntegrationSettings.Developer, true));
        Assert.True(settings.Active(key));
    }

    [Theory]
    [InlineData("gemini", Feature.Gemini)]
    [InlineData("opencode", Feature.OpenCode)]
    public void GeminiAndOpenCodeNameThemselvesOnceHeardAndOnlyWithDeveloperMode(string agent, string feature)
    {
        var settings = Settings(agent);
        var parts = new DashboardParts { Sessions = new SessionBoard(() => DateTimeOffset.UtcNow), AgentHeard = _ => true };
        using var host = Host(settings, parts);
        Assert.DoesNotContain(feature, host.Features());

        Assert.True(settings.SetFlag(IntegrationSettings.Developer, true));
        Assert.Contains(feature, host.Features());
    }

    [Fact]
    public void ASavedTokenHostNamesItselfOnlyWhileItsSwitchAndDeveloperModeAreOn()
    {
        var settings = Settings("gitea");
        var secrets = new MemorySecrets();
        Assert.True(secrets.Set(GitTokens.TokenName("gitea"), "abcdefgh12345678"));
        var parts = new DashboardParts { GitHosts = [new GiteaHost(secrets, settings)] };
        using var host = Host(settings, parts);
        Assert.DoesNotContain(Feature.Gitea, host.Features());

        Assert.True(settings.SetFlag(IntegrationSettings.Developer, true));
        Assert.Contains(Feature.Gitea, host.Features());

        Assert.True(settings.SetFlag(IntegrationSettings.Developer, false));
        Assert.DoesNotContain(Feature.Gitea, host.Features());
    }

    [Fact]
    public void EveryFailedSaveHasASentenceAndOnlySavedHasNone()
    {
        foreach (var status in Enum.GetValues<GitSaveStatus>())
        {
            var message = DevelopersWindow.SaveMessage(status);
            if (status == GitSaveStatus.Saved)
            {
                Assert.Null(message);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(message), status.ToString());
            }
        }
    }

    [Fact]
    public void EveryProviderHasSetupStepsExceptGitLabUntilItsSignInShips() =>
        Assert.All(DevProviders.All, provider => Assert.Equal(provider.Id != "gitlab", DevelopersWindow.StepsFor(provider.Id).Length > 0));

    [Fact]
    public void TheRegistryListsFourAiToolsAndFiveGitHosts()
    {
        Assert.Equal(new[] { "claude", "codex", "gemini", "opencode" }, DevProviders.Of(DevKind.Ai).Select(p => p.Id));
        Assert.Equal(new[] { "github", "gitlab", "gitea", "bitbucket", "gitee" }, DevProviders.Of(DevKind.Git).Select(p => p.Id));
    }
}
