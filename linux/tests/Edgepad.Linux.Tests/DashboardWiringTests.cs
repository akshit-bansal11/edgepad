using Edgepad.Blob;
using Edgepad.Dashboard;
using Edgepad.Net;
using Edgepad.Protocol;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class DashboardWiringTests : IDisposable
{
    private static readonly string[] PluginFiles =
    [
        ".claude-plugin/plugin.json",
        ".codex-plugin/plugin.json",
        "hooks/hooks.claude.json",
        "hooks/hooks.codex.json",
        "hooks/relay.ts",
        "marketplace.template.json",
        "scripts/edgepad-hook.mjs",
        "scripts/scan-claude.mjs",
        "scripts/cache.mjs",
        "scripts/prices.mjs",
    ];

    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-dash");

    public void Dispose() => dir.Delete(recursive: true);

    [Fact]
    public void ThePluginTravelsInsideTheApp()
    {
        var entries = new ResourcePluginSource(typeof(LinuxDashboard).Assembly).Entries();
        foreach (var file in PluginFiles)
        {
            Assert.Contains(file, entries);
        }
    }

    [Fact]
    public void WithEverythingOffNothingIsAddedOrMade()
    {
        using var dashboard = new LinuxDashboard(dir.FullName, "5.0.0");
        dashboard.Start();
        var parts = dashboard.Parts;
        Assert.Null(parts.Sessions);
        Assert.Null(parts.Broker);
        Assert.Null(parts.Codex);
        Assert.Null(parts.Weather);
        Assert.Null(parts.GitHub);
        Assert.False(File.Exists(Path.Combine(dir.FullName, IntegrationSettings.FileName)));

        using var link = new DashboardLink(dashboard, _ => { }, Blobs());
        var features = new Text((byte)TextKind.Features, "annotate/capture");
        var unavailable = new Text((byte)TextKind.Unavailable, string.Empty);
        Assert.Equal(features, link.Annotate(features));
        Assert.Equal(unavailable, link.Annotate(unavailable));
        Assert.False(link.Answer("0123456789ab/a"));
    }

    [Fact]
    public void StatsSwitchedOnIsNamedInFeatures()
    {
        using var dashboard = new LinuxDashboard(dir.FullName, "5.0.0");
        dashboard.Start();
        using var link = new DashboardLink(dashboard, _ => { }, Blobs());
        dashboard.Settings.SetFlag(IntegrationSettings.Stats, true);
        dashboard.Apply();
        var sent = Assert.IsType<Text>(link.Annotate(new Text((byte)TextKind.Features, "capture")));
        Assert.Equal($"capture/{Feature.Stats}", sent.Value);
    }

    [Theory]
    [InlineData("", "", "")]
    [InlineData("capture", "", "capture")]
    [InlineData("", "stats", "stats")]
    [InlineData("capture", "stats/github", "capture/stats/github")]
    public void EntriesJoinWithOneSlash(string text, string more, string expected) =>
        Assert.Equal(expected, DashboardLink.Join(text, more.Split('/', StringSplitOptions.RemoveEmptyEntries)));

    [Fact]
    public void InstallerOutcomesMapOntoTheWindow()
    {
        var manual = new PluginResult(PluginState.ManualSteps, ["claude plugin marketplace add /x", "claude plugin install edgepad@edgepad"]);
        var shown = LinuxDashboard.StateFor(AgentToken.Claude, manual);
        Assert.Equal(AgentStatus.NotConnected, shown.Status);
        Assert.Equal("claude plugin marketplace add /x\nclaude plugin install edgepad@edgepad", shown.Commands);

        Assert.True(LinuxDashboard.StateFor(AgentToken.Claude, PluginResult.Of(PluginState.Connected)).Restart);
        Assert.False(LinuxDashboard.StateFor(AgentToken.Codex, PluginResult.Of(PluginState.Connected)).Restart);
        Assert.Equal(AgentStatus.AwaitingTrust, LinuxDashboard.StateFor(AgentToken.Codex, PluginResult.Of(PluginState.AwaitingTrust)).Status);
        Assert.Equal(AgentStatus.NotFound, LinuxDashboard.StateFor(AgentToken.Codex, PluginResult.Of(PluginState.CliNotFound)).Status);
        Assert.Equal(AgentStatus.NodeMissing, LinuxDashboard.StateFor(AgentToken.Claude, PluginResult.Of(PluginState.NodeMissing)).Status);
        Assert.Equal(AgentStatus.UpdateCodex, LinuxDashboard.StateFor(AgentToken.Codex, PluginResult.Of(PluginState.UpdateCodex)).Status);
        Assert.Equal(AgentStatus.NotConnected, LinuxDashboard.StateFor(AgentToken.Claude, PluginResult.Of(PluginState.Failed)).Status);
    }

    private static BlobSender Blobs() => new(_ => { }, LinkKind.Bluetooth, () => 0, TestContext.Current.CancellationToken);
}
