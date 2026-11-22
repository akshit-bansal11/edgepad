using Edgepad.Dashboard;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class DevelopersWindowTests
{
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
