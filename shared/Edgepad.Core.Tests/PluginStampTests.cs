using System.Text;
using System.Text.Json;
using Edgepad.Dashboard;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class PluginStampTests : IDisposable
{
    private static readonly string?[] HookArgs = ["--hook", "claude", "perm"];

    // A Windows-looking app path with a space, a quote, a backslash run and a non-ASCII letter: the stamp must keep it whole.
    private const string AppPath = @"C:\Pro gram\""q""\nöde\Edgepad.exe";

    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-stamp");

    public void Dispose() => dir.Delete(recursive: true);

    internal static IPluginSource RealBundle()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            var candidate = Path.Combine(at.FullName, "plugins", "edgepad");
            if (File.Exists(Path.Combine(candidate, "hooks", "hooks.claude.json")))
            {
                return new DirectoryPluginSource(candidate);
            }
        }

        throw new DirectoryNotFoundException("plugins/edgepad was not found above the test output directory");
    }

    private string Folder(string agent) => Path.Combine(dir.FullName, "plug ins é", agent);

    private static JsonElement Hooks(string folder) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, PluginStamp.PluginFolder, "hooks", "hooks.json"))).RootElement;

    [Fact]
    public void ClaudeHooksRunTheAppWithItsPathWhole()
    {
        var folder = Folder(AgentToken.Claude);
        PluginStamp.Write(RealBundle(), AgentToken.Claude, folder, AppPath, "5.0.0");

        var hook = Hooks(folder).GetProperty("hooks").GetProperty("PermissionRequest")[0].GetProperty("hooks")[0];
        Assert.Equal(AppPath, hook.GetProperty("command").GetString());
        Assert.Equal(HookArgs, hook.GetProperty("args").EnumerateArray().Select(a => a.GetString()));

        var relay = File.ReadAllText(Path.Combine(folder, PluginStamp.PluginFolder, "hooks", "relay.ts"));
        Assert.Contains("\"" + JsonEncodedText.Encode(AppPath).ToString() + "\"", relay, StringComparison.Ordinal);

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, PluginStamp.PluginFolder, ".claude-plugin", "plugin.json")));
        Assert.Equal("5.0.0", manifest.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public void CodexHooksCarryTheQuotedCommandLine()
    {
        var folder = Folder(AgentToken.Codex);
        PluginStamp.Write(RealBundle(), AgentToken.Codex, folder, AppPath, "5.0.0");

        var command = Hooks(folder).GetProperty("hooks").GetProperty("PermissionRequest")[0].GetProperty("hooks")[0].GetProperty("command").GetString();
        Assert.Equal($"\"{AppPath}\" --hook codex perm", command);
    }

    [Fact]
    public void EveryPlaceholderIsReplaced()
    {
        foreach (var agent in new[] { AgentToken.Claude, AgentToken.Codex })
        {
            var folder = Folder(agent);
            PluginStamp.Write(RealBundle(), agent, folder, AppPath, "5.0.0");
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                foreach (var placeholder in new[] { PluginStamp.App, PluginStamp.Version, "__NODE__", "__ROOT__" })
                {
                    Assert.DoesNotContain(placeholder, text, StringComparison.Ordinal);
                }

                if (file.EndsWith(".json", StringComparison.Ordinal))
                {
                    using var parsed = JsonDocument.Parse(text);
                }
            }
        }
    }

    [Fact]
    public void EachAgentGetsOnlyItsOwnFiles()
    {
        var claude = Folder(AgentToken.Claude);
        var codex = Folder(AgentToken.Codex);
        PluginStamp.Write(RealBundle(), AgentToken.Claude, claude, AppPath, "5.0.0");
        PluginStamp.Write(RealBundle(), AgentToken.Codex, codex, AppPath, "5.0.0");

        string[] Files(string folder) =>
            [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/')).Order(StringComparer.Ordinal)];

        var codexFiles = Files(codex);
        Assert.Contains("edgepad/hooks/hooks.json", codexFiles);
        Assert.Contains("edgepad/.codex-plugin/plugin.json", codexFiles);
        Assert.Contains(".claude-plugin/marketplace.json", codexFiles);
        foreach (var claudeOnly in new[] { "edgepad/hooks/relay.ts", "edgepad/.claude-plugin/plugin.json" })
        {
            Assert.DoesNotContain(claudeOnly, codexFiles);
        }

        Assert.False(Hooks(codex).TryGetProperty("modules", out _));
        Assert.DoesNotContain(codexFiles, f => f.Contains("hooks.claude", StringComparison.Ordinal) || f.Contains("hooks.codex", StringComparison.Ordinal) || f.Contains("template", StringComparison.Ordinal));

        var claudeFiles = Files(claude);
        Assert.Contains("edgepad/hooks/relay.ts", claudeFiles);
        Assert.DoesNotContain("edgepad/.codex-plugin/plugin.json", claudeFiles);

        // Nothing that needs a script runtime ships.
        Assert.DoesNotContain(claudeFiles.Concat(codexFiles), f => f.EndsWith(".mjs", StringComparison.Ordinal) || f.EndsWith(".js", StringComparison.Ordinal));
        Assert.Equal("./relay.ts", Hooks(claude).GetProperty("modules")[0].GetString());

        using var market = JsonDocument.Parse(File.ReadAllText(Path.Combine(codex, ".claude-plugin", "marketplace.json")));
        Assert.Equal("edgepad", market.RootElement.GetProperty("name").GetString());
        Assert.Equal("./edgepad", market.RootElement.GetProperty("plugins")[0].GetProperty("source").GetString());
    }

    [Fact]
    public void AStampIsAlwaysFresh()
    {
        var folder = Folder(AgentToken.Claude);
        PluginStamp.Write(RealBundle(), AgentToken.Claude, folder, AppPath, "5.0.0");
        var planted = Path.Combine(folder, PluginStamp.PluginFolder, "hooks", "planted.mjs");
        File.WriteAllText(planted, "evil");
        File.WriteAllText(Path.Combine(folder, PluginStamp.PluginFolder, "hooks", "hooks.json"), "{}");

        PluginStamp.Write(RealBundle(), AgentToken.Claude, folder, AppPath, "5.0.1");

        Assert.False(File.Exists(planted));
        Assert.True(Hooks(folder).TryGetProperty("hooks", out _));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("scripts/../../evil.txt")]
    [InlineData("/etc/evil.txt")]
    public void AnEntryLeavingTheFolderStopsTheStampAndKeepsTheOldCopy(string entry)
    {
        var folder = Folder(AgentToken.Claude);
        PluginStamp.Write(RealBundle(), AgentToken.Claude, folder, AppPath, "5.0.0");

        var bad = new MemorySource(new() { ["hooks/hooks.claude.json"] = "{}"u8.ToArray(), [entry] = "x"u8.ToArray() });
        Assert.Throws<InvalidDataException>(() => PluginStamp.Write(bad, AgentToken.Claude, folder, AppPath, "5.0.0"));
        Assert.True(File.Exists(Path.Combine(folder, PluginStamp.PluginFolder, "hooks", "relay.ts")));
        Assert.False(File.Exists(Path.Combine(dir.FullName, "evil.txt")));
    }

    [Fact]
    public void AnEntryOver1MiBStopsTheStamp()
    {
        var big = new MemorySource(new()
        {
            ["hooks/hooks.claude.json"] = "{}"u8.ToArray(),
            ["scripts/big.mjs"] = new byte[PluginStamp.MaxEntryBytes + 1],
        });
        Assert.Throws<InvalidDataException>(() => PluginStamp.Write(big, AgentToken.Claude, Folder(AgentToken.Claude), AppPath, "5.0.0"));

        var exact = new MemorySource(new()
        {
            ["hooks/hooks.claude.json"] = "{}"u8.ToArray(),
            ["scripts/big.mjs"] = new byte[PluginStamp.MaxEntryBytes],
        });
        PluginStamp.Write(exact, AgentToken.Claude, Folder(AgentToken.Claude), AppPath, "5.0.0");
    }

    [Fact]
    public void ABundleWithoutTheAgentsHooksIsRefused()
    {
        var none = new MemorySource(new() { ["hooks/hooks.claude.json"] = "{}"u8.ToArray() });
        Assert.Throws<InvalidDataException>(() => PluginStamp.Write(none, AgentToken.Codex, Folder(AgentToken.Codex), AppPath, "5.0.0"));
    }

    [Fact]
    public void TheValuesAreJsonEscapedNotConcatenated()
    {
        var bundle = new MemorySource(new() { ["hooks/hooks.claude.json"] = Encoding.UTF8.GetBytes("""{"n":"__APP__","v":"__VERSION__"}""") });
        var folder = Folder(AgentToken.Claude);
        PluginStamp.Write(bundle, AgentToken.Claude, folder, AppPath, "5.0.0 \"beta\"");

        var hooks = Hooks(folder);
        Assert.Equal(AppPath, hooks.GetProperty("n").GetString());
        Assert.Equal("5.0.0 \"beta\"", hooks.GetProperty("v").GetString());
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Edgepad\Edgepad.exe", null, @"C:\Users\me\AppData\Local\Edgepad\Edgepad.exe")]
    [InlineData(@"C:\Program Files\Edgepad\Edgepad.exe", @"C:\PROGRA~1\Edgepad\Edgepad.exe", @"C:\PROGRA~1\Edgepad\Edgepad.exe")]
    [InlineData(@"C:\Program Files\Edgepad\Edgepad.exe", null, null)]
    [InlineData(@"C:\Program Files\Edgepad\Edgepad.exe", @"C:\Program Files\Edgepad\Edgepad.exe", null)]
    [InlineData(@"C:\a&b\Edgepad.exe", null, null)]
    [InlineData(@"C:\Users\Zoë\Edgepad.exe", null, null)]
    public void OnWindowsCodexGetsABarePathOnlyWhenNoShellWouldReadItDifferently(string app, string? shortForm, string? bare) =>
        Assert.Equal(bare, PluginStamp.BareCodexApp(app, _ => shortForm));

    [Fact]
    public void WithoutAShortPathSeamTheCodexCommandStaysQuoted() =>
        Assert.Null(PluginStamp.BareCodexApp(@"C:\x\Edgepad.exe", null));

    [Fact]
    public void ABareCodexAppReplacesTheQuotedPlaceholder()
    {
        var bundle = new MemorySource(new() { ["hooks/hooks.codex.json"] = Encoding.UTF8.GetBytes("""{"command":"\"__APP__\" --hook codex perm","n":"__APP__"}""") });
        var folder = Folder(AgentToken.Codex);
        PluginStamp.Write(bundle, AgentToken.Codex, folder, AppPath, "5.0.0", @"C:\PROGRA~1\Edgepad\Edgepad.exe");

        var hooks = Hooks(folder);
        Assert.Equal(@"C:\PROGRA~1\Edgepad\Edgepad.exe --hook codex perm", hooks.GetProperty("command").GetString());
        Assert.Equal(AppPath, hooks.GetProperty("n").GetString());
    }

    internal sealed class MemorySource(Dictionary<string, byte[]> files) : IPluginSource
    {
        public IReadOnlyList<string> Entries() => [.. files.Keys];

        public Stream Open(string entry) => new MemoryStream(files[entry], writable: false);
    }
}
