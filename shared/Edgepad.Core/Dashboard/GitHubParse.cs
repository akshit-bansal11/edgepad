using System.Text.Json;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// Reads GitHub's replies into BLOB GITHUB lines. Everything here came from the network: every string is cleaned and cut, a
/// repository that is not plain <c>owner/name</c> drops its row, and a reply that does not read gives null, never a throw.
/// </summary>
internal static class GitHubParse
{
    /// <summary>The GraphQL reply nests ten deep (data, alias, nodes, node, commits, nodes, node, commit, rollup); two to spare.</summary>
    public const int MaxDepth = 12;

    public const int MaxTitle = 80;
    public const int MaxUnread = 50;

    private const int MaxRepoPart = 100;
    private const int MaxLogin = 39;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = MaxDepth };

    /// <summary>Review-requested pull requests as R lines, then the user's own as M lines; null when the reply carries no data.</summary>
    public static IReadOnlyList<GitHubLine>? Pulls(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body, Options);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var lines = new List<GitHubLine>();
            AddSection(lines, data, "review", "R");
            AddSection(lines, data, "mine", "M");
            return lines;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>How many unread notifications a <c>GET /notifications</c> reply lists, at most 50; null when it is not a list.</summary>
    public static int? Unread(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body, Options);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? Math.Min(doc.RootElement.EnumerateArray().Count(n => n.ValueKind == JsonValueKind.Object), MaxUnread)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The login of a <c>GET /user</c> reply when it is a plausible GitHub login (1..39 letters, digits, hyphens); else null.</summary>
    public static string? Login(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body, Options);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && Text(doc.RootElement, "login") is { Length: > 0 and <= MaxLogin } login
                && login.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
                    ? login
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A <c>statusCheckRollup.state</c> as a <see cref="CiToken"/>: SUCCESS ok, FAILURE and ERROR fail, PENDING and EXPECTED pending, anything else none.</summary>
    public static string Ci(string? state) => state switch
    {
        "SUCCESS" => CiToken.Ok,
        "FAILURE" or "ERROR" => CiToken.Fail,
        "PENDING" or "EXPECTED" => CiToken.Pending,
        _ => CiToken.None,
    };

    /// <summary>Exactly <c>owner/name</c>, each part 1..100 of ASCII letters, digits, "_", "." and "-".</summary>
    public static bool IsRepo(string? repo)
    {
        var parts = (repo ?? string.Empty).Split('/');
        return parts.Length == 2 && parts.All(part => part.Length is >= 1 and <= MaxRepoPart
            && part.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'));
    }

    private static void AddSection(List<GitHubLine> lines, JsonElement data, string alias, string section)
    {
        if (!data.TryGetProperty(alias, out var search) || search.ValueKind != JsonValueKind.Object
            || !search.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var added = 0;
        foreach (var pr in nodes.EnumerateArray())
        {
            if (added < GitHubBlob.MaxPerSection && Line(pr, section) is { } line)
            {
                lines.Add(line);
                added++;
            }
        }
    }

    private static GitHubLine? Line(JsonElement pr, string section)
    {
        if (pr.ValueKind != JsonValueKind.Object
            || !pr.TryGetProperty("number", out var n) || n.ValueKind != JsonValueKind.Number || !n.TryGetInt64(out var number) || number <= 0
            || !pr.TryGetProperty("repository", out var repository) || repository.ValueKind != JsonValueKind.Object
            || Text(repository, "nameWithOwner") is not { } repo || !IsRepo(repo))
        {
            return null;
        }

        var title = DashWire.Clean(Text(pr, "title") ?? string.Empty, MaxTitle);
        var draft = pr.TryGetProperty("isDraft", out var d) && d.ValueKind == JsonValueKind.True;
        return new GitHubLine(section, repo, number, title, draft, Ci(RollupState(pr)));
    }

    private static string? RollupState(JsonElement pr)
    {
        if (!pr.TryGetProperty("commits", out var commits) || commits.ValueKind != JsonValueKind.Object
            || !commits.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() == 0)
        {
            return null;
        }

        var last = nodes[nodes.GetArrayLength() - 1];
        return last.ValueKind == JsonValueKind.Object
            && last.TryGetProperty("commit", out var commit) && commit.ValueKind == JsonValueKind.Object
            && commit.TryGetProperty("statusCheckRollup", out var rollup) && rollup.ValueKind == JsonValueKind.Object
                ? Text(rollup, "state")
                : null;
    }

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
