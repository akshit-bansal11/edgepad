namespace Edgepad.Trust;

/// <summary>
/// The phones paired over the network link, each with the 32-byte key its pairing left both ends, one per line:
/// <c>idHex&lt;TAB&gt;base64(key)&lt;TAB&gt;name</c>. A phone that resumes proves it holds its key; one not listed
/// has to pair again, which needs the owner at the laptop.
///
/// The file is read the way <see cref="TrustStore"/> reads its own, for the same reason: a file that is there and
/// cannot be read, or holds a line this class would not have written, refuses every phone rather than being taken
/// for an empty list. Forget in the tray clears it with the trusted phones.
/// </summary>
internal sealed class PairedLinks(string path)
{
    private const int KeyLength = 32;

    private static readonly char[] LineBreaks = ['\r', '\n'];

    private readonly Lock gate = new();

    // Set when Forget could not delete or empty the file: every phone is refused until it can, as TrustStore does.
    private bool forgetFailed;

    // Moved on by every Forget. A pairing reads it when it starts and hands it to Add, so a Forget chosen while the
    // phone was pairing is the later word.
    private int generation;

    /// <summary>Moved on by every <see cref="Forget"/>; read by a pairing as it starts, for <see cref="Add"/>.</summary>
    public int Generation
    {
        get
        {
            lock (gate)
            {
                return generation;
            }
        }
    }

    public static PairedLinks ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgepad", "paired-links.txt"));

    /// <summary>The key and name a phone was paired with; false for a phone not paired, and for every phone while the file cannot be read.</summary>
    public bool TryGet(string idHex, out byte[] key, out string name)
    {
        lock (gate)
        {
            key = [];
            name = "";
            if (!TryRead(out var links))
            {
                return false;
            }

            var found = links.Find(link => string.Equals(link.Id, idHex, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                return false;
            }

            key = [.. found.Key];
            name = found.Name;
            return true;
        }
    }

    /// <summary>
    /// Records a phone's pairing, replacing an older one for the same id. False when it could not be written, the
    /// file could not be read (written over, it would lose the phones the read could not see), or the pairings were
    /// forgotten since <paramref name="since"/> was read from <see cref="Generation"/>; the caller tells the last
    /// from the others by <see cref="Generation"/>.
    /// </summary>
    public bool Add(string idHex, byte[] key, string name, int? since = null)
    {
        if (key.Length != KeyLength)
        {
            throw new ArgumentException($"A pairing key is {KeyLength} bytes", nameof(key));
        }

        lock (gate)
        {
            if ((since is { } started && started != generation) || !TryRead(out var links))
            {
                return false;
            }

            links.RemoveAll(link => string.Equals(link.Id, idHex, StringComparison.OrdinalIgnoreCase));

            // The name is the phone's own say, shown only; a tab or line break in it would end its line early.
            var clean = string.Concat(name.Select(c => char.IsControl(c) ? ' ' : c));
            links.Add(new Link(idHex, key, clean));
            try
            {
                PrivateStorage.Replace(path, temp => File.WriteAllLines(
                    temp,
                    links.Select(link => $"{link.Id}\t{Convert.ToBase64String(link.Key)}\t{link.Name}")));
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Write($"A paired phone could not be saved to {path}: {e.Message}");
                return false;
            }
        }
    }

    /// <summary>True when there is something for Forget to clear: a paired phone, or a file that cannot be read.</summary>
    public bool CanForget
    {
        get
        {
            lock (gate)
            {
                return !TryRead(out var links) || links.Count > 0;
            }
        }
    }

    /// <summary>
    /// True when every pairing is gone: the file deleted or, failing that, emptied. False when neither worked; every
    /// phone is then refused for this run, as for an unreadable file.
    /// </summary>
    public bool Forget()
    {
        lock (gate)
        {
            generation++;
            try
            {
                File.Delete(path);
            }
            catch (DirectoryNotFoundException)
            {
                // Nothing was ever paired.
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                try
                {
                    File.WriteAllText(path, "");
                    Log.Write($"The paired phones' file could not be deleted at {path} and was emptied instead: {e.Message}");
                }
                catch (Exception emptyError) when (emptyError is IOException or UnauthorizedAccessException)
                {
                    Log.Write($"The paired phones could not be forgotten at {path}: {e.Message}");
                    forgetFailed = true;
                    return false;
                }
            }

            forgetFailed = false;
            return true;
        }
    }

    /// <summary>
    /// False when the file is there and cannot be read, holds a line this class would not have written, or a Forget
    /// failed; the caller refuses. An absent or blank file is no phone paired. Called under the lock.
    /// </summary>
    private bool TryRead(out List<Link> links)
    {
        links = [];
        if (forgetFailed)
        {
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The paired phones could not be read from {path}, so every network phone is refused: {e.Message}");
            return false;
        }

        foreach (var line in text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParse(line, out var link))
            {
                // The line itself is never logged: it holds a key.
                Log.Write($"The paired phones in {path} are not in a form Edgepad writes, so every network phone is refused until they are forgotten");
                links = [];
                return false;
            }

            links.Add(link);
        }

        return true;
    }

    private static bool TryParse(string line, out Link link)
    {
        link = new Link("", [], "");
        var parts = line.Split('\t', 3);
        if (parts.Length != 3 || parts[0].Length != 32 || !parts[0].All(char.IsAsciiHexDigit))
        {
            return false;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        link = new Link(parts[0], key, parts[2]);
        return key.Length == KeyLength;
    }

    private sealed record Link(string Id, byte[] Key, string Name);
}
