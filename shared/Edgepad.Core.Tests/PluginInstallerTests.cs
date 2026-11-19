using Edgepad.Dashboard;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class PluginInstallerTests : IDisposable
{
    private const string Ok = """{"command":"x","outcome":"ok"}""";

    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-install");
    private readonly IntegrationSettings settings;
    private readonly FakeRunner runner;
    private string nodeVersion = "v22.3.0";
    private string codexVersion = "codex-cli 0.159.0";
    private Func<string[], ProcessResult> cliReply = _ => new ProcessResult(0, Ok);

    public PluginInstallerTests()
    {
        settings = new IntegrationSettings(Path.Combine(dir.FullName, IntegrationSettings.FileName));
        Bin = Directory.CreateDirectory(Path.Combine(dir.FullName, "bin")).FullName;
        Node = Touch(OperatingSystem.IsWindows() ? "node.exe" : "node");
        Claude = Touch(OperatingSystem.IsWindows() ? "claude.exe" : "claude");
        Codex = Touch(OperatingSystem.IsWindows() ? "codex.cmd" : "codex");
        runner = new FakeRunner((file, args) =>
            file == Node ? new ProcessResult(0, nodeVersion)
            : file == Codex && args is ["--version"] ? new ProcessResult(0, codexVersion)
            : cliReply(args));
    }

    private string Bin { get; }

    private string Node { get; }

    private string Claude { get; }

    private string Codex { get; }

    private string Root => Path.Combine(dir.FullName, "plugins");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => dir.Delete(recursive: true);

    private string Touch(string name)
    {
        var file = Path.Combine(Bin, name);
        File.WriteAllText(file, string.Empty);
        return file;
    }

    private PluginInstaller New(string? root = null, string version = "5.0.0", string? path = null) =>
        new(PluginStampTests.RealBundle(), settings, root ?? Root, version, runner, () => path ?? Bin);

    private List<(string File, string[] Args, string Cwd)> CliCalls() => [.. runner.Calls.Where(c => c.File != Node)];

    [Fact]
    public async Task ClaudeConnectRunsTheTwoDocumentedCommands()
    {
        var installer = New();
        var result = await installer.ConnectAsync(AgentToken.Claude, Ct);

        Assert.Equal(PluginState.Connected, result.State);
        var folder = installer.FolderOf(AgentToken.Claude);
        Assert.Equal<(string, string[], string)>(
            [
                (Claude, new[] { "plugin", "marketplace", "add", folder, "--json" }, folder),
                (Claude, new[] { "plugin", "install", "edgepad@edgepad", "--scope", "user", "--json" }, folder),
            ],
            CliCalls(),
            new CallComparer());
        Assert.True(settings.Flag(IntegrationSettings.Claude));
        Assert.Equal("5.0.0", settings.Text(IntegrationSettings.PluginVersionKey(AgentToken.Claude)));
        Assert.Equal(Node, settings.Text(IntegrationSettings.PluginNodeKey(AgentToken.Claude)));
        Assert.True(File.Exists(Path.Combine(folder, ".claude-plugin", "marketplace.json")));
        Assert.Equal(PluginState.Connected, installer.Status(AgentToken.Claude));
    }

    [Fact]
    public async Task CodexConnectChecksTheVersionThenWaitsForTrust()
    {
        settings.SetFlag(IntegrationSettings.CodexTrusted, true);
        var installer = New();
        var result = await installer.ConnectAsync(AgentToken.Codex, Ct);

        Assert.Equal(PluginState.AwaitingTrust, result.State);
        var folder = installer.FolderOf(AgentToken.Codex);
        Assert.Equal<(string, string[], string)>(
            [
                (Codex, new[] { "--version" }, folder),
                (Codex, new[] { "plugin", "marketplace", "add", folder, "--json" }, folder),
                (Codex, new[] { "plugin", "add", "edgepad@edgepad", "--json" }, folder),
            ],
            CliCalls(),
            new CallComparer());
        Assert.Equal(PluginState.AwaitingTrust, installer.Status(AgentToken.Codex));
        settings.SetFlag(IntegrationSettings.CodexTrusted, true);
        Assert.Equal(PluginState.Connected, installer.Status(AgentToken.Codex));
        Assert.DoesNotContain(runner.Calls, c => c.Args.Any(a => a.Contains("bypass", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("codex-cli 0.158.9", PluginState.UpdateCodex)]
    [InlineData("codex-cli 0.159.0", PluginState.AwaitingTrust)]
    [InlineData("codex-cli 1.0.0", PluginState.AwaitingTrust)]
    [InlineData("codex-cli 0.160.0-alpha.2", PluginState.AwaitingTrust)]
    [InlineData("garbage", PluginState.UpdateCodex)]
    [InlineData("", PluginState.UpdateCodex)]
    public async Task CodexBelow0159AsksForAnUpdate(string reported, PluginState expected)
    {
        codexVersion = reported;
        Assert.Equal(expected, (await New().ConnectAsync(AgentToken.Codex, Ct)).State);
        Assert.Equal(expected == PluginState.AwaitingTrust, settings.Flag(IntegrationSettings.Codex));
    }

    [Theory]
    [InlineData("v20.1.0", true)]
    [InlineData("v20.0.9", false)]
    [InlineData("v18.0.0", false)]
    [InlineData("v22.11.0", true)]
    [InlineData("20.1.0", false)]
    [InlineData("garbage", false)]
    public void NodeVersionsParse(string text, bool enough) =>
        Assert.Equal(enough, PluginInstaller.NodeVersion(text) is { } v && v >= PluginInstaller.MinNode);

    [Fact]
    public void CodexVersionsParse()
    {
        Assert.Equal(new Version(0, 159, 0), PluginInstaller.CodexVersion("codex-cli 0.159.0"));
        Assert.Null(PluginInstaller.CodexVersion("codex 0.159.0"));
        Assert.Null(PluginInstaller.CodexVersion("codex-cli 0.159"));
        Assert.Null(PluginInstaller.CodexVersion("codex-cli 0.159.0; rm -rf /"));
    }

    [Fact]
    public async Task AnOldOrMissingNodeIsNodeMissing()
    {
        nodeVersion = "v18.0.0";
        Assert.Equal(PluginState.NodeMissing, (await New().ConnectAsync(AgentToken.Claude, Ct)).State);
        Assert.Empty(CliCalls());
        Assert.Equal(PluginState.NodeMissing, (await New(path: Path.Combine(dir.FullName, "empty")).ConnectAsync(AgentToken.Claude, Ct)).State);
    }

    [Fact]
    public async Task AMissingCliIsCliNotFoundWithTheFolderNamed()
    {
        File.Delete(Claude);
        var installer = New();
        var result = await installer.ConnectAsync(AgentToken.Claude, Ct);
        Assert.Equal(PluginState.CliNotFound, result.State);
        Assert.Contains(result.Commands, c => c.Contains(installer.FolderOf(AgentToken.Claude), StringComparison.Ordinal));
        Assert.False(settings.Flag(IntegrationSettings.Claude));
    }

    [Theory]
    [InlineData("a&b")]
    [InlineData("a%PATH%b")]
    [InlineData("a^b")]
    [InlineData("a!b")]
    [InlineData("a(b)")]
    public async Task AnUnsafeFolderShowsTheCommandsAndRunsNothing(string name)
    {
        var result = await New(root: Path.Combine(dir.FullName, name)).ConnectAsync(AgentToken.Claude, Ct);
        Assert.Equal(PluginState.ManualSteps, result.State);
        Assert.NotEmpty(result.Commands);
        Assert.Empty(runner.Calls);
        Assert.False(Directory.Exists(Path.Combine(dir.FullName, name)));
    }

    [Theory]
    [InlineData(0, """{"outcome":"failed","failureCode":"x"}""")]
    [InlineData(0, "not json")]
    [InlineData(0, "")]
    [InlineData(0, """{"message":"no outcome"}""")]
    [InlineData(1, """{"outcome":"ok"}""")]
    [InlineData(-1, "")]
    public async Task AnythingButOkIsManualSteps(int exit, string line)
    {
        cliReply = _ => new ProcessResult(exit, line);
        var result = await New().ConnectAsync(AgentToken.Claude, Ct);
        Assert.Equal(PluginState.ManualSteps, result.State);
        Assert.Equal(2, result.Commands.Count);
        Assert.False(settings.Flag(IntegrationSettings.Claude));
    }

    [Fact]
    public async Task CodexAcceptsJsonWithoutAnOutcomeButNotTextOrATimeout()
    {
        cliReply = _ => new ProcessResult(0, """{"installed":true}""");
        Assert.Equal(PluginState.AwaitingTrust, (await New().ConnectAsync(AgentToken.Codex, Ct)).State);
        cliReply = _ => new ProcessResult(0, "Installed edgepad");
        Assert.Equal(PluginState.ManualSteps, (await New().ConnectAsync(AgentToken.Codex, Ct)).State);
        cliReply = _ => new ProcessResult(-1, string.Empty, TimedOut: true);
        Assert.Equal(PluginState.ManualSteps, (await New().ConnectAsync(AgentToken.Codex, Ct)).State);
    }

    [Fact]
    public void ThePathScanSkipsRelativeFolders()
    {
        var relative = "edgepad-relpath-" + Guid.NewGuid().ToString("N");
        var local = Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, relative));
        try
        {
            File.WriteAllText(Path.Combine(local.FullName, OperatingSystem.IsWindows() ? "claude.exe" : "claude"), string.Empty);
            Assert.Null(PluginInstaller.FindCli(AgentToken.Claude, relative + Path.PathSeparator + "." + Path.PathSeparator + string.Empty));
            Assert.Equal(Claude, PluginInstaller.FindCli(AgentToken.Claude, relative + Path.PathSeparator + Bin));
        }
        finally
        {
            local.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheCopyBoxQuotesAFolderWithSpaces()
    {
        var installer = New(root: Path.Combine(dir.FullName, "my plugins"));
        var folder = installer.FolderOf(AgentToken.Claude);
        Assert.Equal<string>(
            [$"claude plugin marketplace add \"{folder}\" --json", "claude plugin install edgepad@edgepad --scope user --json"],
            installer.Commands(AgentToken.Claude));
        Assert.Equal<string>(
            ["codex plugin remove edgepad@edgepad --json", "codex plugin marketplace remove edgepad --json"],
            installer.Commands(AgentToken.Codex, connect: false));
    }

    [Fact]
    public async Task DisconnectUninstallsAndDeletesTheCopy()
    {
        var installer = New();
        await installer.ConnectAsync(AgentToken.Claude, Ct);
        runner.Calls.Clear();

        var result = await installer.DisconnectAsync(AgentToken.Claude, Ct);

        Assert.Equal(PluginState.NotConnected, result.State);
        Assert.Equal<string[]>(
            [["plugin", "uninstall", "edgepad@edgepad", "--scope", "user", "--json"], ["plugin", "marketplace", "remove", "edgepad", "--json"]],
            CliCalls().Select(c => c.Args).ToList());
        Assert.False(Directory.Exists(installer.FolderOf(AgentToken.Claude)));
        Assert.False(settings.Flag(IntegrationSettings.Claude));
        Assert.Equal(string.Empty, settings.Text(IntegrationSettings.PluginVersionKey(AgentToken.Claude)));
    }

    [Fact]
    public async Task AFailedDisconnectKeepsTheCopyButTurnsTheSwitchOff()
    {
        var installer = New();
        await installer.ConnectAsync(AgentToken.Claude, Ct);
        cliReply = _ => new ProcessResult(1, string.Empty);

        var result = await installer.DisconnectAsync(AgentToken.Claude, Ct);

        Assert.Equal(PluginState.ManualSteps, result.State);
        Assert.Equal(installer.Commands(AgentToken.Claude, connect: false), result.Commands);
        Assert.True(Directory.Exists(installer.FolderOf(AgentToken.Claude)));
        Assert.False(settings.Flag(IntegrationSettings.Claude));
    }

    [Fact]
    public async Task EnsureCurrentDoesNothingWhenTheStampMatches()
    {
        await New().ConnectAsync(AgentToken.Claude, Ct);
        runner.Calls.Clear();

        Assert.Empty(await New().EnsureCurrentAsync(Ct));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task EnsureCurrentRestampsAfterAnUpdateWithoutTheCli()
    {
        var installer = New();
        await installer.ConnectAsync(AgentToken.Claude, Ct);
        var hooks = Path.Combine(installer.FolderOf(AgentToken.Claude), "edgepad", "hooks", "hooks.json");
        File.WriteAllText(hooks, "{}");
        runner.Calls.Clear();

        var notices = await New(version: "5.0.1").EnsureCurrentAsync(Ct);

        Assert.Equal<(string, PluginState)>([(AgentToken.Claude, PluginState.RestartSessions)], notices);
        Assert.Empty(CliCalls());
        Assert.Equal("5.0.1", settings.Text(IntegrationSettings.PluginVersionKey(AgentToken.Claude)));
        Assert.Contains("PermissionRequest", File.ReadAllText(hooks), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureCurrentRestampsWhenTheStampedNodeIsGone()
    {
        await New().ConnectAsync(AgentToken.Claude, Ct);
        settings.Set(IntegrationSettings.PluginNodeKey(AgentToken.Claude), Path.Combine(dir.FullName, "gone", "node"));

        Assert.Equal<(string, PluginState)>([(AgentToken.Claude, PluginState.RestartSessions)], await New().EnsureCurrentAsync(Ct));
        Assert.Equal(Node, settings.Text(IntegrationSettings.PluginNodeKey(AgentToken.Claude)));

        settings.Set(IntegrationSettings.PluginVersionKey(AgentToken.Claude), "4.9.0");
        Assert.Equal<(string, PluginState)>([(AgentToken.Claude, PluginState.NodeMissing)], await New(path: Path.Combine(dir.FullName, "empty")).EnsureCurrentAsync(Ct));
    }

    [Fact]
    public async Task TheRealRunnerKillsAProgramPastItsTime()
    {
        var (file, args) = OperatingSystem.IsWindows()
            ? (Path.Combine(Environment.SystemDirectory, "PING.EXE"), new[] { "-n", "30", "127.0.0.1" })
            : ("/bin/sleep", new[] { "30" });
        var started = Environment.TickCount64;

        var result = await new ProcessRunner().RunAsync(file, args, Path.GetTempPath(), TimeSpan.FromMilliseconds(500), Ct);

        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
        Assert.InRange(Environment.TickCount64 - started, 0, 15_000);
    }

    [Fact]
    public async Task TheRealRunnerReportsAMissingProgramAsMinusOne()
    {
        var result = await new ProcessRunner().RunAsync(Path.Combine(dir.FullName, "no-such-program"), [], Path.GetTempPath(), TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public void TheLastNonEmptyLineIsTheOneParsed() =>
        Assert.Equal("""{"outcome":"ok"}""", ProcessRunner.LastLine("Installing…\r\n{\"outcome\":\"ok\"}\r\n\r\n"));

    private sealed class FakeRunner(Func<string, string[], ProcessResult> reply) : IProcessRunner
    {
        public List<(string File, string[] Args, string Cwd)> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
        {
            string[] args = [.. arguments];
            Calls.Add((file, args, workingDirectory));
            return Task.FromResult(reply(file, args));
        }
    }

    private sealed class CallComparer : IEqualityComparer<(string File, string[] Args, string Cwd)>
    {
        public bool Equals((string File, string[] Args, string Cwd) x, (string File, string[] Args, string Cwd) y) =>
            x.File == y.File && x.Args.SequenceEqual(y.Args) && x.Cwd == y.Cwd;

        public int GetHashCode((string File, string[] Args, string Cwd) obj) => obj.File.GetHashCode(StringComparison.Ordinal);
    }
}
