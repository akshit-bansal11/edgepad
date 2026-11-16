using System.Globalization;
using Edgepad.Trust;

namespace Edgepad.Updates;

/// <summary>
/// <c>update-choice.txt</c>: what the owner said to an offered update. Lines <c>skip=&lt;version&gt;</c>,
/// <c>later=&lt;unix seconds&gt;</c> and <c>notified=&lt;version&gt;</c>. Read once and kept; an unreadable or
/// garbled file reads as "nothing chosen", so a damaged file never hides an update for good.
/// </summary>
internal sealed class UpdateChoice
{
    private const long LaterSeconds = 24 * 60 * 60;

    private readonly string path;
    private readonly Lock gate = new();
    private Version? skip;
    private long later;
    private Version? notified;

    public UpdateChoice(string path)
    {
        this.path = path;
        Load();
    }

    /// <summary>
    /// False when <paramref name="version"/> was skipped, or "later" was said within a day of <paramref name="now"/>.
    /// A "later" further in the future than that is ignored, so a damaged file cannot hide updates for good.
    /// </summary>
    public bool ShouldShow(Version version, DateTimeOffset now)
    {
        lock (gate)
        {
            var at = now.ToUnixTimeSeconds();
            var waiting = later > at - LaterSeconds && later <= at + LaterSeconds;
            return skip != version && !waiting;
        }
    }

    public bool Skip(Version version)
    {
        lock (gate)
        {
            skip = version;
            return Save();
        }
    }

    public bool Later(DateTimeOffset now)
    {
        lock (gate)
        {
            later = now.ToUnixTimeSeconds();
            return Save();
        }
    }

    public bool MarkNotified(Version version)
    {
        lock (gate)
        {
            notified = version;
            return Save();
        }
    }

    public bool WasNotified(Version version)
    {
        lock (gate)
        {
            return notified == version;
        }
    }

    private void Load()
    {
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var split = line.IndexOf('=', StringComparison.Ordinal);
                if (split < 0)
                {
                    continue;
                }

                var value = line[(split + 1)..].Trim();
                switch (line[..split].Trim())
                {
                    case "skip" when Version.TryParse(value, out var s):
                        skip = s;
                        break;
                    case "later" when long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var l):
                        later = l;
                        break;
                    case "notified" when Version.TryParse(value, out var n):
                        notified = n;
                        break;
                    default:
                        break;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The update choice could not be read from {path}, so nothing is chosen: {e.Message}");
        }
    }

    private bool Save()
    {
        try
        {
            PrivateStorage.Replace(path, temp => File.WriteAllLines(temp, Lines()));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The update choice could not be saved to {path}: {e.Message}");
            return false;
        }
    }

    private IEnumerable<string> Lines()
    {
        if (skip is not null)
        {
            yield return "skip=" + skip;
        }

        if (later > 0)
        {
            yield return "later=" + later.ToString(CultureInfo.InvariantCulture);
        }

        if (notified is not null)
        {
            yield return "notified=" + notified;
        }
    }
}
