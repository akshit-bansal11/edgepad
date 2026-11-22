using System.Text.Json.Nodes;
using Edgepad.Dashboard;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class HookConfigInstallerTests : IDisposable
{
    private const string UserSettings = """
        {
          "theme": "dark",
          "hooks": {
            "BeforeAgent": [
              { "matcher": "*", "hooks": [ { "name": "mine", "type": "command", "command": "echo hi" } ] }
            ],
            "AfterTool": [
              { "hooks": [ { "name": "other", "type": "command", "command": "echo tool" } ] }
            ]
          },
          "mcpServers": { "a": { "command": "x", "args": [ 1, 2.50 ] } }
        }
        """;

    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-hookcfg");
    private readonly IntegrationSettings settings;

    public HookConfigInstallerTests() => settings = new IntegrationSettings(Path.Combine(dir.FullName, IntegrationSettings.FileName));

    private static string App => OperatingSystem.IsWindows() ? @"C:\Program Files\Edgepad\Edgepad.exe" : "/opt/edgepad/edgepad";

    public void Dispose() => dir.Delete(recursive: true);

    private HookConfigInstaller New(string version = "5.0.0", string? app = null) =>
        new(PluginStampTests.RealBundle(), settings, Path.Combine(dir.FullName, "home"), version, app ?? App);

    private static JsonNode Read(string file) => JsonNode.Parse(File.ReadAllText(file))!;

    private static string Write(string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    private static List<JsonNode> Ours(JsonNode root) =>
        [.. root["hooks"]!.AsObject()
            .SelectMany(e => e.Value!.AsArray())
            .SelectMany(g => g!["hooks"]!.AsArray())
            .OfType<JsonNode>()
            .Where(h => h["name"]?.GetValue<string>() == "edgepad")];

    [Fact]
    public void GeminiConnectOnAMissingFileWritesOnlyOurFiveEntries()
    {
        var installer = New();

        Assert.Equal(PluginState.Connected, installer.Connect(AgentToken.Gemini).State);

        var root = Read(installer.GeminiFile);
        Assert.Equal("AfterAgent,BeforeAgent,Notification,SessionEnd,SessionStart", string.Join(',', root["hooks"]!.AsObject().Select(e => e.Key).Order(StringComparer.Ordinal)));
        Assert.Equal(5, Ours(root).Count);
        Assert.Equal($"\"{App}\" --hook gemini session", Ours(root)[0]["command"]!.GetValue<string>());
        Assert.True(settings.Flag(AgentToken.Gemini));
        Assert.False(File.Exists(installer.GeminiFile + HookConfigInstaller.BackupSuffix));
    }

    [Fact]
    public void GeminiConnectKeepsEveryOtherKeyAndBacksUpTheOldFile()
    {
        var installer = New();
        Write(installer.GeminiFile, UserSettings);

        installer.Connect(AgentToken.Gemini);

        var root = Read(installer.GeminiFile);
        Assert.Equal("dark", root["theme"]!.GetValue<string>());
        Assert.Equal("echo tool", root["hooks"]!["AfterTool"]![0]!["hooks"]![0]!["command"]!.GetValue<string>());
        Assert.Equal("mine", root["hooks"]!["BeforeAgent"]![0]!["hooks"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(2, root["hooks"]!["BeforeAgent"]!.AsArray().Count);
        Assert.Contains("2.50", File.ReadAllText(installer.GeminiFile), StringComparison.Ordinal);
        Assert.Equal(UserSettings, File.ReadAllText(installer.GeminiFile + HookConfigInstaller.BackupSuffix));
    }

    [Fact]
    public void ReconnectAndDisconnectKeepTheFirstBackupAndTheKeyOrder()
    {
        var installer = New();
        Write(installer.GeminiFile, UserSettings);
        installer.Connect(AgentToken.Gemini);
        New(version: "5.1.0", app: App + "x").Connect(AgentToken.Gemini);
        installer.Disconnect(AgentToken.Gemini);

        Assert.Equal(UserSettings, File.ReadAllText(installer.GeminiFile + HookConfigInstaller.BackupSuffix));
        var text = File.ReadAllText(installer.GeminiFile);
        Assert.True(text.IndexOf("\"theme\"", StringComparison.Ordinal) < text.IndexOf("\"hooks\"", StringComparison.Ordinal));
        Assert.True(text.IndexOf("\"hooks\"", StringComparison.Ordinal) < text.IndexOf("\"mcpServers\"", StringComparison.Ordinal));
        Assert.Contains("2.50", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GeminiConnectTwiceIsTheSameFile()
    {
        var installer = New();
        Write(installer.GeminiFile, UserSettings);
        installer.Connect(AgentToken.Gemini);
        var once = File.ReadAllText(installer.GeminiFile);

        installer.Connect(AgentToken.Gemini);

        Assert.Equal(once, File.ReadAllText(installer.GeminiFile));
        Assert.Equal(5, Ours(Read(installer.GeminiFile)).Count);
    }

    [Fact]
    public void GeminiDisconnectLeavesTheUsersEntriesAndNothingOfOurs()
    {
        var installer = New();
        Write(installer.GeminiFile, UserSettings);
        installer.Connect(AgentToken.Gemini);

        Assert.Equal(PluginState.NotConnected, installer.Disconnect(AgentToken.Gemini).State);

        var after = Read(installer.GeminiFile);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(UserSettings), after));
        Assert.False(settings.Flag(AgentToken.Gemini));
    }

    [Fact]
    public void GeminiDisconnectWithNoFileIsFine()
    {
        var installer = New();

        Assert.Equal(PluginState.NotConnected, installer.Disconnect(AgentToken.Gemini).State);
        Assert.False(File.Exists(installer.GeminiFile));
    }

    [Theory]
    [InlineData("{ \"theme\": \"dark\", }")]
    [InlineData("{ // note\n \"theme\": \"dark\" }")]
    [InlineData("[1,2]")]
    [InlineData("not json")]
    [InlineData("{ \"hooks\": 3 }")]
    [InlineData("{ \"hooks\": { \"BeforeAgent\": {} } }")]
    public void GeminiRefusesAConfigItCannotRewriteSafelyAndLeavesItAlone(string text)
    {
        var installer = New();
        Write(installer.GeminiFile, text);

        Assert.Equal(PluginState.Failed, installer.Connect(AgentToken.Gemini).State);

        Assert.Equal(text, File.ReadAllText(installer.GeminiFile));
        Assert.False(settings.Flag(AgentToken.Gemini));
        Assert.False(File.Exists(installer.GeminiFile + HookConfigInstaller.BackupSuffix));
    }

    [Fact]
    public void GeminiDisconnectOfAnUnparseableFileReportsAndKeepsItAndTheSwitchOff()
    {
        var installer = New();
        settings.SetFlag(AgentToken.Gemini, true);
        Write(installer.GeminiFile, "{ broken");

        Assert.Equal(PluginState.Failed, installer.Disconnect(AgentToken.Gemini).State);

        Assert.Equal("{ broken", File.ReadAllText(installer.GeminiFile));
        Assert.False(settings.Flag(AgentToken.Gemini));
    }

    [Fact]
    public void GeminiRefusesAnAppPathThatCouldBreakTheCommandLine()
    {
        var installer = New(app: "/opt/a\"b; rm -rf /");

        Assert.Equal(PluginState.Failed, installer.Connect(AgentToken.Gemini).State);
        Assert.False(File.Exists(installer.GeminiFile));
    }

    [Fact]
    public void OpenCodeConnectWritesTheStampedPluginAndDisconnectRemovesIt()
    {
        var installer = New();

        Assert.Equal(PluginState.Connected, installer.Connect(AgentToken.OpenCode).State);

        var text = File.ReadAllText(installer.OpenCodeFile);
        Assert.StartsWith("// " + HookConfigInstaller.OpenCodeMarker, text, StringComparison.Ordinal);
        Assert.DoesNotContain("__APP__", text, StringComparison.Ordinal);
        Assert.DoesNotContain("__VERSION__", text, StringComparison.Ordinal);
        Assert.Contains("version 5.0.0", text, StringComparison.Ordinal);
        Assert.Contains($"const APP = \"{System.Text.Json.JsonEncodedText.Encode(App)}\"", text, StringComparison.Ordinal);

        Assert.Equal(PluginState.NotConnected, installer.Disconnect(AgentToken.OpenCode).State);
        Assert.False(File.Exists(installer.OpenCodeFile));
    }

    [Fact]
    public void OpenCodeNeverOverwritesOrDeletesAFileThatIsNotOurs()
    {
        var installer = New();
        Write(installer.OpenCodeFile, "// my own plugin\n");

        Assert.Equal(PluginState.Failed, installer.Connect(AgentToken.OpenCode).State);
        installer.Disconnect(AgentToken.OpenCode);

        Assert.Equal("// my own plugin\n", File.ReadAllText(installer.OpenCodeFile));
    }

    [Fact]
    public void EnsureCurrentRewritesAfterAnUpdateAndIsQuietOtherwise()
    {
        New().Connect(AgentToken.OpenCode);
        Assert.Empty(New().EnsureCurrent());

        var notices = New(version: "5.1.0").EnsureCurrent();

        var notice = Assert.Single(notices);
        Assert.Equal(AgentToken.OpenCode, notice.Agent);
        Assert.Equal(PluginState.RestartSessions, notice.State);
        Assert.Contains("version 5.1.0", File.ReadAllText(New().OpenCodeFile), StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeAndCodexStampsCarryNeitherTemplate()
    {
        foreach (var agent in new[] { AgentToken.Claude, AgentToken.Codex })
        {
            var folder = Path.Combine(dir.FullName, "stamp", agent);
            PluginStamp.Write(PluginStampTests.RealBundle(), agent, folder, App, "5.0.0");

            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => f.Replace('\\', '/')).ToList();
            Assert.DoesNotContain(files, f => f.EndsWith("hooks.gemini.json", StringComparison.Ordinal) || f.Contains("/opencode/", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void GeminiEventsMapToTheBoard()
    {
        Assert.Equal(SessionEvent.Start, HookEvent.FromGemini("SessionStart", null, "g1", "/p")?.Event);
        Assert.Equal(SessionEvent.Prompt, HookEvent.FromGemini("BeforeAgent", null, "g1", "/p")?.Event);
        Assert.Equal(SessionEvent.NeedsYou, HookEvent.FromGemini("Notification", "ToolPermission", "g1", "/p")?.Event);
        Assert.Equal(SessionEvent.Stop, HookEvent.FromGemini("AfterAgent", null, "g1", "/p")?.Event);
        Assert.Equal(SessionEvent.End, HookEvent.FromGemini("SessionEnd", null, "g1", "/p")?.Event);
        Assert.Null(HookEvent.FromGemini("Notification", "Other", "g1", "/p"));
        Assert.Null(HookEvent.FromGemini("BeforeTool", null, "g1", "/p"));
        Assert.Equal(AgentToken.Gemini, HookEvent.FromGemini("SessionStart", null, "g1", "/p")?.Agent);
    }

    [Fact]
    public void OpenCodeEventsMapToTheBoard()
    {
        Assert.Equal(SessionEvent.Start, HookEvent.FromOpenCode("session.created", "o1", "/p")?.Event);
        Assert.Equal(SessionEvent.Prompt, HookEvent.FromOpenCode("session.busy", "o1", "/p")?.Event);
        Assert.Equal(SessionEvent.NeedsYou, HookEvent.FromOpenCode("permission.asked", "o1", "/p")?.Event);
        Assert.Equal(SessionEvent.Stop, HookEvent.FromOpenCode("session.idle", "o1", "/p")?.Event);
        Assert.Equal(SessionEvent.End, HookEvent.FromOpenCode("session.deleted", "o1", "/p")?.Event);
        Assert.Null(HookEvent.FromOpenCode("message.updated", "o1", "/p"));
        Assert.Equal(AgentToken.OpenCode, HookEvent.FromOpenCode("session.idle", "o1", "/p")?.Agent);
    }
}
