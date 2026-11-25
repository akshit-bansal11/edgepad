namespace Edgepad.Macros;

/// <summary>
/// Rendered macro icons, keyed by path, last-write time and size together, so a rebuilt program or a replaced icon
/// file is picked up rather than served stale for the life of the process, and a phone reconnecting costs no work at
/// all. A null result is kept too, so a file with no picture is read once and not on every connect. The Windows and
/// Linux icon readers each hold one; they differ only in how a file becomes a PNG.
/// </summary>
/// <remarks>
/// Safe from any thread. Two callers may render the same icon at once, which costs a duplicate read and never a wrong
/// answer: the render runs outside the lock so one slow file does not hold up every other icon.
/// </remarks>
// ponytail: never evicted; one entry per path, write time and size. A tray app left running for weeks while the same
// program is rebuilt keeps every icon it ever made (a few kilobytes each). Bound it if that ever shows.
internal sealed class IconCache
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, byte[]?> cache = [];

    /// <summary>
    /// The PNG for <paramref name="path"/> at <paramref name="size"/>: from the cache when the file has not been written
    /// since, otherwise from <paramref name="render"/>, whose answer is then kept. Null when the file's write time
    /// cannot be read, which is not cached, or when <paramref name="render"/> gives null, which is.
    /// </summary>
    public byte[]? Get(string path, int size, Func<string, int, byte[]?> render)
    {
        string key;
        try
        {
            key = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}|{size}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        lock (gate)
        {
            if (cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var png = render(path, size);
        lock (gate)
        {
            cache[key] = png;
        }

        return png;
    }
}
