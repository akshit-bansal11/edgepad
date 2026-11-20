namespace Edgepad.Controls;

/// <summary>
/// VLC 3 does not report to the system media transport controls, so what it plays is read from its window title:
/// "song.mp3 - VLC media player" while a file is open, exactly "VLC media player" when idle.
/// </summary>
internal static class VlcTitle
{
    internal const string Suffix = " - VLC media player";

    /// <summary>The media title from a VLC window title, or null when VLC is idle or the title is not VLC's.</summary>
    internal static string? Parse(string windowTitle)
    {
        if (windowTitle is null || !windowTitle.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return null;
        }

        var media = windowTitle[..^Suffix.Length].Trim();
        return media.Length == 0 ? null : media;
    }
}
