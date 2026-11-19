using System.Reflection;
using System.Text;
using System.Text.Json;
using Edgepad.Protocol;
using Edgepad.Trust;

namespace Edgepad.Dashboard;

/// <summary>The plugin source folder (plugins/edgepad) as the app carries it: relative paths with "/", and their bytes.</summary>
internal interface IPluginSource
{
    /// <summary>Every file, as a path relative to the plugin folder.</summary>
    IReadOnlyList<string> Entries();

    /// <summary>The bytes of one entry from <see cref="Entries"/>.</summary>
    Stream Open(string entry);
}

/// <summary>The plugin folder embedded in an assembly, each file a manifest resource named <c>plugins/edgepad/&lt;path&gt;</c>.</summary>
internal sealed class ResourcePluginSource(Assembly assembly, string prefix = ResourcePluginSource.Prefix) : IPluginSource
{
    public const string Prefix = "plugins/edgepad/";

    public IReadOnlyList<string> Entries() =>
        [.. assembly.GetManifestResourceNames().Select(n => n.Replace('\\', '/')).Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).Select(n => n[prefix.Length..])];

    public Stream Open(string entry)
    {
        var name = assembly.GetManifestResourceNames().First(n => n.Replace('\\', '/') == prefix + entry);
        return assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException(entry);
    }
}

/// <summary>The plugin folder on disk (a Mac bundle's Resources, a test's copy of plugins/edgepad).</summary>
internal sealed class DirectoryPluginSource(string root) : IPluginSource
{
    public IReadOnlyList<string> Entries() =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))];

    public Stream Open(string entry) => File.OpenRead(Path.Combine(root, entry));
}

/// <summary>
/// Writes the per-agent copy of the plugin that Connect points the vendor CLI at, <c>&lt;plugins&gt;/&lt;agent&gt;/</c>:
/// <c>edgepad/</c> with the agent's hooks template as <c>hooks/hooks.json</c> and only the files that agent uses, plus
/// <c>.claude-plugin/marketplace.json</c> beside it. Always deleted and written fresh, so a tampered copy reverts. The placeholders
/// <c>__NODE__</c>, <c>__ROOT__</c> (the copy's own folder, forward slashes) and <c>__VERSION__</c> are replaced by values escaped by
/// the JSON encoder, which is also a valid escape inside relay.ts's double-quoted string. An entry whose path would leave the folder,
/// or one over 1 MiB, stops the stamp with <see cref="InvalidDataException"/>.
/// </summary>
internal static class PluginStamp
{
    public const int MaxEntryBytes = 1024 * 1024;
    public const string PluginFolder = "edgepad";
    public const string Marketplace = ".claude-plugin/marketplace.json";
    public const string Node = "__NODE__";
    public const string Root = "__ROOT__";
    public const string Version = "__VERSION__";

    private const string MarketplaceTemplate = "marketplace.template.json";
    private const string ClaudeHooks = "hooks/hooks.claude.json";
    private const string CodexHooks = "hooks/hooks.codex.json";
    private const string Hooks = "hooks/hooks.json";

    // Files only Claude Code loads: its manifest, the usage mod and the spend scanner. Codex has its own manifest folder.
    private static readonly string[] ClaudeOnly = [".claude-plugin/", "hooks/relay.ts", "scripts/scan-claude.mjs", "scripts/cache.mjs", "scripts/prices.mjs", "types/"];
    private static readonly string[] CodexOnly = [".codex-plugin/"];

    /// <summary>The <c>__ROOT__</c> value for a stamp in <paramref name="folder"/>: its <c>edgepad</c> folder, absolute, with forward slashes.</summary>
    public static string RootOf(string folder) => Path.GetFullPath(Path.Combine(folder, PluginFolder)).Replace('\\', '/');

    /// <summary>Deletes <paramref name="folder"/> and writes the agent's copy there. Throws what the disk threw, or <see cref="InvalidDataException"/> for a bad bundle.</summary>
    public static void Write(IPluginSource bundle, string agent, string folder, string nodePath, string version)
    {
        if (!DashWire.IsAgent(agent))
        {
            throw new ArgumentException("Not an agent", nameof(agent));
        }

        var full = Path.GetFullPath(folder);
        var root = Path.Combine(full, PluginFolder);
        var values = new Dictionary<string, string>
        {
            [Node] = JsonEncodedText.Encode(nodePath).ToString(),
            [Root] = JsonEncodedText.Encode(RootOf(full)).ToString(),
            [Version] = JsonEncodedText.Encode(version).ToString(),
        };

        // Read and check everything before the old copy goes, so a bad bundle leaves the working one in place.
        var files = new List<(string Target, byte[] Bytes)>();
        foreach (var raw in bundle.Entries())
        {
            var entry = raw.Replace('\\', '/');
            if (Target(agent, entry) is not { } relative)
            {
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(relative == Marketplace ? full : root, relative));
            if (Path.IsPathRooted(entry) || entry.Split('/').Contains("..") || !target.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException("A plugin file would land outside its folder");
            }

            var bytes = Read(bundle, raw);
            if (relative.EndsWith(".json", StringComparison.Ordinal) || relative.EndsWith(".ts", StringComparison.Ordinal))
            {
                bytes = Encoding.UTF8.GetBytes(Fill(Encoding.UTF8.GetString(bytes), values));
            }

            files.Add((target, bytes));
        }

        if (files.Count == 0 || !files.Exists(f => f.Target.EndsWith(Path.Combine("hooks", "hooks.json"), StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The plugin bundle has no hooks template for this agent");
        }

        Remove(full);
        PrivateStorage.CreateDirectory(full);
        foreach (var (target, bytes) in files)
        {
            PrivateStorage.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
    }

    /// <summary>Deletes a stamped copy. A link is removed as a link, never followed.</summary>
    public static void Remove(string folder)
    {
        var info = new DirectoryInfo(folder);
        if (info.LinkTarget is not null)
        {
            info.Delete();
        }
        else if (info.Exists)
        {
            info.Delete(recursive: true);
        }
    }

    // Where an entry goes for this agent (relative to edgepad/, or the marketplace file beside it); null when not copied.
    private static string? Target(string agent, string entry)
    {
        if (entry == MarketplaceTemplate)
        {
            return Marketplace;
        }

        if (entry is ClaudeHooks or CodexHooks)
        {
            return entry == (agent == AgentToken.Claude ? ClaudeHooks : CodexHooks) ? Hooks : null;
        }

        var other = agent == AgentToken.Claude ? CodexOnly : ClaudeOnly;
        return other.Any(p => p.EndsWith('/') ? entry.StartsWith(p, StringComparison.Ordinal) : entry == p) ? null : entry;
    }

    private static byte[] Read(IPluginSource bundle, string entry)
    {
        using var stream = bundle.Open(entry);
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = stream.Read(buffer)) > 0)
        {
            copy.Write(buffer, 0, n);
            if (copy.Length > MaxEntryBytes)
            {
                throw new InvalidDataException("A plugin file is over 1 MiB");
            }
        }

        return copy.ToArray();
    }

    private static string Fill(string text, Dictionary<string, string> values)
    {
        foreach (var (placeholder, value) in values)
        {
            text = text.Replace(placeholder, value, StringComparison.Ordinal);
        }

        return text;
    }
}
