using System.Text;
using System.Text.RegularExpressions;

namespace Edgepad.Media;

/// <summary>
/// The track title as the phone shows it: brackets, "feat." tails and a repeated artist are cut. Pure, and called
/// once per track change. The Swift port reads the same fixture, protocol/titles.txt, so the two cannot drift.
/// </summary>
internal static partial class TitleCleaner
{
    private static readonly char[] Separators = [' ', '-', '–', '—', '|', ':'];
    private static readonly char[] Dashes = ['-', '–', '—'];

    /// <summary>"Title - Artist", the way the callers have always built it, with the title cleaned.</summary>
    public static string NowPlaying(string title, string artist) =>
        string.IsNullOrWhiteSpace(artist) ? Clean(title, artist) : Clean(title, artist) + " - " + artist;

    /// <summary>
    /// <paramref name="title"/> without brackets, featured-artist tails and a repeated leading artist.
    /// <paramref name="artist"/> may be a joined list "A, B". An empty result returns the original title.
    /// </summary>
    public static string Clean(string title, string artist)
    {
        var text = StripBrackets(title);

        var feat = Featured().Match(text);
        if (feat.Success)
        {
            text = text[..feat.Index];
        }

        var whole = Name(artist);
        var listed = artist.Split(',').Select(Name).Where(name => name.Length > 0).ToList();
        var names = new HashSet<string>(listed, StringComparer.OrdinalIgnoreCase);
        foreach (var name in listed)
        {
            names.UnionWith(NameSplit().Split(name).Select(Name).Where(part => part.Length > 0));
        }

        if (names.Count > 0)
        {
            text = CutArtistTail(text, names);
            text = DropLeadingArtist(text, whole, listed);
        }

        text = Whitespace().Replace(text, " ").Trim(Separators);
        return text.Length == 0 ? title : text;
    }

    private static string StripBrackets(string text)
    {
        var kept = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var end = IsOpener(text[i]) ? ClosingIndex(text, i) : -1;
            if (end < 0)
            {
                kept.Append(text[i]);
            }
            else
            {
                i = end;
            }
        }

        return kept.ToString();
    }

    private static bool IsOpener(char c) => c is '(' or '[' or '{';

    /// <summary>The index of the bracket closing the one at <paramref name="start"/>; -1 when unbalanced or crossed.</summary>
    private static int ClosingIndex(string text, int start)
    {
        var expected = new Stack<char>();
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (IsOpener(c))
            {
                expected.Push(c == '(' ? ')' : c == '[' ? ']' : '}');
            }
            else if (c is ')' or ']' or '}')
            {
                if (expected.Pop() != c)
                {
                    return -1;
                }

                if (expected.Count == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static string CutArtistTail(string text, HashSet<string> names)
    {
        foreach (var joiner in Joiner().EnumerateMatches(text))
        {
            var rest = text[(joiner.Index + joiner.Length)..];
            var pieces = NameSplit().Split(rest).Select(Name).Where(part => part.Length > 0).ToList();
            if (pieces.Count > 0 && pieces.All(names.Contains))
            {
                return text[..joiner.Index];
            }
        }

        return text;
    }

    private static string DropLeadingArtist(string text, string whole, List<string> listed)
    {
        var trimmed = text.TrimStart();
        foreach (var name in listed.Prepend(whole))
        {
            if (name.Length > 0 && trimmed.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                var rest = trimmed[name.Length..].TrimStart();
                if (rest.Length > 0 && Array.IndexOf(Dashes, rest[0]) >= 0)
                {
                    return rest[1..];
                }
            }
        }

        return text;
    }

    /// <summary>An artist or a name in a tail, without the " - Topic" or "VEVO" a channel adds.</summary>
    private static string Name(string name)
    {
        var clean = name.Trim();
        if (clean.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[..^8].TrimEnd();
        }

        if (clean.EndsWith("VEVO", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[..^4].TrimEnd();
        }

        return clean;
    }

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:featuring|feat|ft|prod)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Featured();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:with|and|x)(?![\p{L}\p{N}])|[&,]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Joiner();

    [GeneratedRegex(@"\s*[,&]\s*|(?<![\p{L}\p{N}])(?:and|x|with)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NameSplit();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
