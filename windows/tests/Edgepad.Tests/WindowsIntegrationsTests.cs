using Edgepad.Blob;
using Edgepad.Dashboard;
using Edgepad.Net;
using Edgepad.Protocol;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class WindowsIntegrationsTests : IDisposable
{
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-integrations-");

    public void Dispose() => dir.Delete(recursive: true);

    private static BlobSender Blobs() => new(_ => { }, LinkKind.Bluetooth, () => 0, CancellationToken.None, () => long.MaxValue, _ => true);

    [Theory]
    [InlineData(".claude-plugin/plugin.json")]
    [InlineData(".codex-plugin/plugin.json")]
    [InlineData("hooks/hooks.claude.json")]
    [InlineData("hooks/hooks.codex.json")]
    [InlineData("hooks/relay.ts")]
    [InlineData("marketplace.template.json")]
    public void EveryPluginTemplateIsEmbedded(string entry) =>
        Assert.Contains(entry, new ResourcePluginSource(typeof(WindowsIntegrations).Assembly).Entries());

    [Fact]
    public void TheEmbeddedPluginIsExactlyTheSourceFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "plugins", "edgepad")))
        {
            folder = folder.Parent;
        }

        Assert.NotNull(folder);
        var onDisk = new DirectoryPluginSource(Path.Combine(folder.FullName, "plugins", "edgepad")).Entries().Order(StringComparer.Ordinal);
        var embedded = new ResourcePluginSource(typeof(WindowsIntegrations).Assembly).Entries().Order(StringComparer.Ordinal);
        Assert.Equal(onDisk, embedded);
    }

    [Fact]
    public void WithEverySwitchOffAPhoneGetsAnEmptyLinkThatHearsDeveloperMode()
    {
        using var integrations = new WindowsIntegrations(dir.FullName, "5.0.0");
        integrations.Start();
        using (var link = integrations.Attach(_ => { }, Blobs()))
        {
            Assert.NotNull(link);
            Assert.Empty(link.Host.Features());

            integrations.SetDeveloper(true);
            Assert.Equal(Feature.Developer, Assert.Single(link.Host.Features()));
        }

        Assert.Equal(-1, integrations.LastHeard(AgentToken.Claude));
        Assert.Equal(AgentState.NotConnected, integrations.Status(AgentToken.Claude).State);
    }

    [Fact]
    public void StatsOnNamesStatsAndNetInfoAndNothingElse()
    {
        using var integrations = new WindowsIntegrations(dir.FullName, "5.0.0");
        Assert.True(integrations.Settings.SetFlag(IntegrationSettings.Stats, true));
        integrations.Changed(IntegrationSettings.Stats);

        var link = integrations.Attach(_ => { }, Blobs());
        Assert.NotNull(link);
        Assert.Equal([Feature.Stats, Feature.NetInfo], link.Host.Features());
        Assert.Empty(link.Host.Unavailable());
        Assert.Null(link.Broker);
        link.Dispose();
        link.Dispose();
    }

    [Theory]
    [InlineData((int)PluginState.Connected, (int)AgentState.Connected, false)]
    [InlineData((int)PluginState.RestartSessions, (int)AgentState.Connected, true)]
    [InlineData((int)PluginState.AwaitingTrust, (int)AgentState.AwaitingTrust, false)]
    [InlineData((int)PluginState.ManualSteps, (int)AgentState.ManualSteps, false)]
    [InlineData((int)PluginState.CliNotFound, (int)AgentState.NotFound, false)]
    [InlineData((int)PluginState.UpdateCodex, (int)AgentState.UpdateCodex, false)]
    [InlineData((int)PluginState.Failed, (int)AgentState.NotConnected, false)]
    [InlineData((int)PluginState.NotConnected, (int)AgentState.NotConnected, false)]
    public void InstallerResultsMapToRows(int from, int to, bool restart)
    {
        var status = WindowsIntegrations.Map(new PluginResult((PluginState)from, ["codex plugin add edgepad@edgepad --json"]));
        Assert.Equal((AgentState)to, status.State);
        Assert.Equal(restart, status.Restart);
        Assert.Equal((AgentState)to is AgentState.ManualSteps or AgentState.NotFound ? "codex plugin add edgepad@edgepad --json" : string.Empty, status.Commands);
    }
}
