using System.Globalization;

namespace Edgepad.Trust;

/// <summary>
/// When each trusted phone last connected, one <c>address&lt;TAB&gt;ISO-8601 UTC</c> line each, in a file beside the
/// trust file (so builds that never heard of it read the trust file as before). Stamped once per verified connection.
/// A write that fails is logged and never blocks the connection; a missing or unreadable entry reads as unknown.
/// </summary>
internal sealed class LastConnected(string path)
{
    private readonly Lock gate = new();

    /// <summary>The file beside <paramref name="trustFile"/>.</summary>
    public static LastConnected Beside(string trustFile) =>
        new(Path.Combine(Path.GetDirectoryName(trustFile) ?? ".", "last-connected.txt"));

    /// <summary>Notes that <paramref name="address"/> connected at <paramref name="at"/>.</summary>
    public void Record(string address, DateTimeOffset at)
    {
        lock (gate)
        {
            // An unreadable file is left as it is: writing what could not be read would wipe every other phone's time.
            if (Read() is not { } all)
            {
                return;
            }

            all[address.ToLowerInvariant()] = at.ToUniversalTime();
            Write(all);
        }
    }

    /// <summary>When <paramref name="address"/> last connected, or null when that is not known.</summary>
    public DateTimeOffset? Get(string address) => Snapshot()(address);

    /// <summary>Every time, read once, for a list that shows many phones: one file read per list, not per row.</summary>
    public Func<string, DateTimeOffset?> Snapshot()
    {
        Dictionary<string, DateTimeOffset> all;
        lock (gate)
        {
            all = Read() ?? [];
        }

        return address => all.TryGetValue(address.ToLowerInvariant(), out var at) ? at : null;
    }

    public void Forget(string address)
    {
        lock (gate)
        {
            if (Read() is { } all && all.Remove(address.ToLowerInvariant()))
            {
                Write(all);
            }
        }
    }

    public void ForgetAll()
    {
        lock (gate)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Write($"The last-connected times could not be removed at {path}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// "NAME · last connected 8 Nov", with the year when it is not this year's; "NAME · not connected since this
    /// update" when the time is not known. The local date (or <paramref name="zone"/>), in English like the rest of the menu.
    /// </summary>
    public static string RowText(string name, DateTimeOffset? last, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        if (last is not { } at)
        {
            return $"{name} · not connected since this update";
        }

        zone ??= TimeZoneInfo.Local;
        var shown = TimeZoneInfo.ConvertTime(at, zone);
        var format = shown.Year == TimeZoneInfo.ConvertTime(now, zone).Year ? "d MMM" : "d MMM yyyy";
        return $"{name} · last connected {shown.ToString(format, CultureInfo.InvariantCulture)}";
    }

    /// <summary>Every stored time; empty when there is no file yet, null when the file is there but cannot be read.</summary>
    private Dictionary<string, DateTimeOffset>? Read()
    {
        var all = new Dictionary<string, DateTimeOffset>();
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return all;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The last-connected times could not be read from {path}: {e.Message}");
            return null;
        }

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length == 2
                && DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            {
                all[parts[0]] = at;
            }
        }

        return all;
    }

    private void Write(Dictionary<string, DateTimeOffset> all)
    {
        try
        {
            PrivateStorage.Replace(
                path,
                temp => File.WriteAllLines(
                    temp, all.Select(entry => entry.Key + "\t" + entry.Value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The last-connected time could not be saved to {path}: {e.Message}");
        }
    }
}
