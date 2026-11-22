using System.Globalization;
using Edgepad.Dashboard;
using Edgepad.Protocol;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class DevRowsTests : IDisposable
{
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-devrows");

    public void Dispose() => dir.Delete(recursive: true);

    [Fact]
    public void EveryFailedSaveHasASentenceAndOnlySavedHasNone()
    {
        foreach (var status in Enum.GetValues<GitSaveStatus>())
        {
            var message = DevRows.SaveMessage(status);
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
        Assert.All(DevProviders.All, provider => Assert.Equal(provider.Id != "gitlab", DevRows.StepsFor(provider.Id).Length > 0));

    [Fact]
    public void TheRegistryListsFourAiToolsAndFiveGitHosts()
    {
        Assert.Equal(new[] { "claude", "codex", "gemini", "opencode" }, DevProviders.Of(DevKind.Ai).Select(p => p.Id));
        Assert.Equal(new[] { "github", "gitlab", "gitea", "bitbucket", "gitee" }, DevProviders.Of(DevKind.Git).Select(p => p.Id));
    }

    [Fact]
    public void AnAiRowOffersConnectUntilConnectedThenDisconnect()
    {
        var now = DateTimeOffset.Now;
        var off = DevRows.Ai("Gemini CLI", new AgentState(AgentStatus.NotConnected), busy: false, now);
        Assert.NotNull(off.Go);
        Assert.Null(off.Undo);
        Assert.True(off.CanHowTo);
        Assert.False(off.Dot);

        var on = DevRows.Ai("Gemini CLI", new AgentState(AgentStatus.Connected, now), busy: false, now);
        Assert.Null(on.Go);
        Assert.NotNull(on.Undo);
        Assert.False(on.CanHowTo);
        Assert.True(on.Dot);
    }

    [Fact]
    public void AnAiRowThatIsLoadingOrBusyOffersNothingToPress()
    {
        var loading = DevRows.Ai("OpenCode", null, busy: false, DateTimeOffset.Now);
        Assert.False(loading.GoEnabled);
        Assert.False(loading.CanHowTo);

        var busy = DevRows.Ai("OpenCode", new AgentState(AgentStatus.NotConnected), busy: true, DateTimeOffset.Now);
        Assert.False(busy.GoEnabled);
        Assert.False(busy.CanHowTo);
    }

    [Fact]
    public void GitLabIsComingSoonAndDisabled()
    {
        var row = DevRows.GitLab();
        Assert.False(row.GoEnabled);
        Assert.False(row.CanHowTo);
        Assert.Null(row.Undo);
    }

    [Fact]
    public void GitHubRowFollowsItsSignIn()
    {
        Assert.False(DevRows.GitHub("GitHub", configured: false, false, false, false, string.Empty).GoEnabled);

        var signing = DevRows.GitHub("GitHub", true, false, signingIn: true, codeShown: true, string.Empty);
        Assert.Null(signing.Go);
        Assert.NotNull(signing.Undo);

        var done = DevRows.GitHub("GitHub", true, signedIn: true, false, false, "octo");
        Assert.True(done.Dot);
        Assert.Contains("octo", done.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void ATokenRowOffersAddUntilSignedIn()
    {
        Assert.NotNull(DevRows.Token("Gitea", false, string.Empty, saving: false).Go);
        var done = DevRows.Token("Gitea", true, "me", saving: false);
        Assert.Null(done.Go);
        Assert.NotNull(done.Undo);
    }

    [Fact]
    public void HeardSaysNeverTodayAndTheDate()
    {
        var now = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(LinuxStrings.Integrations_Never, DevRows.Heard(null, now, CultureInfo.InvariantCulture));
        Assert.Equal(now.ToLocalTime().ToString("t", CultureInfo.InvariantCulture), DevRows.Heard(now, now, CultureInfo.InvariantCulture));
        Assert.Contains("Nov", DevRows.Heard(now.AddDays(-3), now, CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gemini")]
    [InlineData("opencode")]
    public void TheHookChannelOpensForGeminiAndOpenCodeOnlyWithDeveloperMode(string agent)
    {
        using var dashboard = new LinuxDashboard(dir.FullName, "5.0.0");
        dashboard.Start();
        dashboard.Settings.SetFlag(agent, true);
        dashboard.Apply();
        Assert.Null(dashboard.Parts.Sessions);

        dashboard.SetDeveloper(true);
        Assert.NotNull(dashboard.Parts.Sessions);

        dashboard.SetDeveloper(false);
        Assert.Null(dashboard.Parts.Sessions);
    }

    [Fact]
    public void RepeatingTheDeveloperSwitchChangesNothingAndRebuildsNoPieces()
    {
        using var dashboard = new LinuxDashboard(dir.FullName, "5.0.0");
        dashboard.Start();
        var raised = 0;
        dashboard.DeveloperChanged += () => raised++;

        dashboard.SetDeveloper(true);
        var parts = dashboard.Parts;
        dashboard.SetDeveloper(true);
        dashboard.SetDeveloper(true);

        Assert.Same(parts, dashboard.Parts);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void GeminiAndOpenCodeAskForARestartLikeClaudeAndCodexDoesNot()
    {
        var connected = PluginResult.Of(PluginState.Connected);
        Assert.True(LinuxDashboard.StateFor(AgentToken.Gemini, connected).Restart);
        Assert.True(LinuxDashboard.StateFor(AgentToken.OpenCode, connected).Restart);
        Assert.False(LinuxDashboard.StateFor(AgentToken.Codex, connected).Restart);
    }
}
