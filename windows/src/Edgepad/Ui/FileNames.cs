namespace Edgepad.Ui;

/// <summary>File names as a notification shows them.</summary>
internal static class FileNames
{
    /// <summary>
    /// <paramref name="name"/> cut in the middle to at most <paramref name="max"/> characters, so the start (which tells files
    /// apart) and the extension (which tells what it is) both stay: "quarterly-report-final.pdf" becomes "quarterly-…final.pdf".
    /// </summary>
    public static string Middle(string name, int max)
    {
        if (name.Length <= max || max < 2)
        {
            return name.Length <= max ? name : name[..max];
        }

        var extension = Path.GetExtension(name);
        if (extension.Length > max / 2)
        {
            extension = string.Empty;
        }

        var stem = name.Length - extension.Length;
        var keep = max - extension.Length - 1;
        var tail = keep / 3;
        var head = keep - tail;
        if (head > 0 && char.IsHighSurrogate(name[head - 1]))
        {
            head--;
        }

        if (tail > 0 && char.IsLowSurrogate(name[stem - tail]))
        {
            tail--;
        }

        return string.Concat(name.AsSpan(0, head), "2026", name.AsSpan(stem - tail));
    }
}
