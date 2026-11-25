namespace Edgepad.Platform;

/// <summary>Reading a <c>.desktop</c> file, as the Desktop Entry spec lays it out.</summary>
internal static class DesktopEntry
{
    /// <summary>
    /// The <c>key=value</c> lines of the <c>[Desktop Entry]</c> group, in file order, each side trimmed. Lines before
    /// it, blank lines, comments and lines with no key are skipped, and reading stops at the next group: later groups
    /// (<c>[Desktop Action ...]</c>) carry their own keys and are not the app's. Values are returned raw, unescaped.
    /// </summary>
    internal static IEnumerable<(string Key, string Value)> MainGroup(IEnumerable<string> lines)
    {
        var inEntry = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                if (inEntry)
                {
                    yield break;
                }

                inEntry = line == "[Desktop Entry]";
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (!inEntry || equals <= 0)
            {
                continue;
            }

            yield return (line[..equals].TrimEnd(), line[(equals + 1)..].Trim());
        }
    }
}
