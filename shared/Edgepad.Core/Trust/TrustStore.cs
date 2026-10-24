namespace Edgepad.Trust;

/// <summary>
/// The phones this laptop takes input from, each one with the owner's say-so. Pairing already limits connections
/// to bonded devices; this narrows them to the phones the owner chose, so some other paired device cannot drive
/// the laptop. A phone that completes the handshake and is not on the list is not taken on its word: the owner is
/// asked, on the laptop, and only a yes writes it down, one address per line. Any number of phones can be trusted
/// that way, each asked about once. The tray menu's Forget clears them all, and each is asked about again the
/// next time it connects.
///
/// Every file operation here is guarded, and the guards are not all the same. Reading is the one that
/// matters: a file that is there and cannot be read is <em>not</em> the same as no file, and treating it
/// as one would re-arm the question for every phone while the file was locked, and write a list of one over
/// the phones the read could not see the moment the owner said yes. So a failed read refuses every phone, and
/// only a genuinely absent file is an empty list.
/// </summary>
internal sealed class TrustStore(string path, Func<long>? clock = null)
{
    /// <summary>
    /// How long a phone the owner just said no to is turned away unasked. Long enough that a phone's automatic
    /// retries cannot put the question straight back under the owner's cursor.
    /// </summary>
    private static readonly TimeSpan QuietAfterDecline = TimeSpan.FromSeconds(30);

    // One address per line, so a file edited by hand reads the same with Windows or Unix line endings.
    private static readonly char[] LineBreaks = ['\r', '\n'];

    private readonly Lock gate = new();

    // Milliseconds, from a clock that only moves forward; a test hands in its own.
    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);

    // The phones admitted when their addresses could not be written down. Kept for this run, so the owner is
    // not asked again about a phone they already said yes to just because the disk refused the write.
    private readonly HashSet<string> unsaved = new(StringComparer.OrdinalIgnoreCase);

    // The phone the owner last said no to, and until when it is not asked about again.
    private string? declined;
    private long declinedUntil;

    // Set while the owner is being asked about one phone. A second phone arriving then is refused unasked
    // rather than stacking a second question on the first, which the owner could answer for the wrong phone.
    private bool asking;

    // Set when Forget could not delete the file. Until it really is gone every phone is refused, exactly as for
    // a file that cannot be read: the phones are still trusted on disk, and the owner believes they are not.
    private bool forgetFailed;

    // Bumped by every Forget. A question that was open across one belongs to a list the owner has since cleared,
    // and the Forget is the later word, so its yes writes nothing down.
    private int generation;

    public static TrustStore ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgepad", "trusted-phone.txt"));

    /// <summary>Where the trusted phones are kept, for a message that has to send the owner there.</summary>
    public string FilePath => path;

    /// <summary>The trusted addresses, for messages and the log; empty when there are none or they cannot be read.</summary>
    public IReadOnlyList<string> TrustedPhones
    {
        get
        {
            lock (gate)
            {
                if (!TryRead(out var trusted))
                {
                    return [];
                }

                return [.. trusted.Union(unsaved, StringComparer.OrdinalIgnoreCase)];
            }
        }
    }

    /// <summary>
    /// True when <paramref name="address"/> may connect. A phone not yet trusted is put to the owner through
    /// <paramref name="ask"/> and a yes is remembered; a trusted one is admitted without it being called. It
    /// blocks for as long as the owner takes, so it runs outside the lock: the tray menu reads
    /// <see cref="TrustedPhones"/> on the very thread that is showing the question, and would otherwise wait on
    /// its own answer.
    /// </summary>
    public bool Admit(string address, Func<bool> ask)
    {
        int askedIn;
        lock (gate)
        {
            if (Decided(address) is { } known)
            {
                return known;
            }

            if (IsDeclined(address))
            {
                return false;
            }

            if (asking)
            {
                return false;
            }

            asking = true;
            askedIn = generation;
        }

        bool yes;
        try
        {
            yes = ask();
        }
        finally
        {
            lock (gate)
            {
                asking = false;
            }
        }

        if (!yes)
        {
            lock (gate)
            {
                declined = address;
                declinedUntil = now() + (long)QuietAfterDecline.TotalMilliseconds;
            }

            return false;
        }

        lock (gate)
        {
            if (generation != askedIn)
            {
                Log.Write($"Did not trust {address}: every trusted phone was forgotten while the owner was being asked");
                return false;
            }

            // Settled again after the wait: a file edited by hand in that minute is still the one that counts.
            if (!TryRead(out var trusted))
            {
                return false;
            }

            if (Holds(trusted, address))
            {
                return true;
            }

            try
            {
                PrivateStorage.Replace(path, temp => File.WriteAllLines(temp, trusted.Append(address)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Admitted anyway, and remembered until the app quits. Refusing the owner's own phone
                // because the disk is full would be a worse answer than forgetting it at the next start.
                unsaved.Add(address);
                Log.Write($"A trusted phone could not be saved to {path}: {e.Message}");
            }

            return true;
        }
    }

    /// <summary>
    /// Whether <paramref name="address"/> is admitted without asking: true for a trusted phone, false for every
    /// phone while the trust file cannot be read, null for a phone the owner has not been asked about yet.
    /// Called under the lock.
    /// </summary>
    private bool? Decided(string address)
    {
        if (!TryRead(out var trusted))
        {
            // Fail closed. The phone sees a refusal, the log says why, and Forget in the tray menu is the way
            // out; the alternative is a laptop that quietly re-pairs itself whenever its own trust file is
            // unreadable, which is the single thing this class exists to prevent.
            return false;
        }

        if (Holds(trusted, address))
        {
            return true;
        }

        return null;
    }

    /// <summary>Whether <paramref name="address"/> is trusted, on disk or for this run. Called under the lock.</summary>
    private bool Holds(List<string> trusted, string address) =>
        unsaved.Contains(address) || trusted.Contains(address, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the owner said no to <paramref name="address"/> recently. Called under the lock.</summary>
    private bool IsDeclined(string address) =>
        declined is not null && now() < declinedUntil && string.Equals(declined, address, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when <paramref name="address"/> is certain to be refused: the owner is being asked about a phone right
    /// now, or just said no to this one, or the trust file cannot be read. A connection arriving during the
    /// question, or from a phone the owner just declined, is turned away without disturbing the phone being asked
    /// about. A phone that is simply not trusted yet is not refused here: it is asked about. Unlike
    /// <see cref="Admit"/> it never grants trust.
    /// </summary>
    public bool Refuses(string address)
    {
        lock (gate)
        {
            return asking || IsDeclined(address) || !TryRead(out _);
        }
    }

    /// <summary>
    /// True when there is something for Forget to clear: a trusted phone, or a trust file that cannot be read,
    /// which refuses every phone until it is removed.
    /// </summary>
    public bool CanForget
    {
        get
        {
            lock (gate)
            {
                return !TryRead(out var trusted) || trusted.Count > 0 || unsaved.Count > 0;
            }
        }
    }

    /// <summary>
    /// True when every phone is forgotten: the trust file is gone (deleted or never there) or, when it could not be
    /// deleted, emptied, which reads the same. False when neither worked: the phones are then still named on disk,
    /// so every phone is refused for this run, as for an unreadable file, until a later Forget succeeds, and those
    /// phones would be trusted again after a restart. A question open while this runs saves nothing.
    /// </summary>
    public bool Forget()
    {
        lock (gate)
        {
            generation++;
            unsaved.Clear();
            declined = null;
            try
            {
                File.Delete(path);
            }
            catch (DirectoryNotFoundException)
            {
                // Delete is silent about a missing file and not about a missing folder, which is the state
                // before any phone was ever trusted. Nothing is there either way.
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // This runs from a click on a tray menu item, where an unhandled exception ends the
                // process. The menu item failing to do its one job is not worth the app, but pretending it
                // worked would leave phones trusted that the owner believes are forgotten. A blank file already
                // means nothing is trusted, and emptying needs different rights from deleting (a handle that
                // allows writing but not deleting, say), so that is tried before giving up.
                try
                {
                    File.WriteAllText(path, "");
                    Log.Write($"The trusted phones' file could not be deleted at {path} and was emptied instead: {e.Message}");
                }
                catch (Exception emptyError) when (emptyError is IOException or UnauthorizedAccessException)
                {
                    Log.Write($"The trusted phones could not be forgotten at {path}: {e.Message}");
                    forgetFailed = true;
                    return false;
                }
            }

            forgetFailed = false;
            return true;
        }
    }

    /// <summary>
    /// False when the file is there and could not be read, or a Forget could not delete it — the caller must
    /// then refuse rather than guess.
    /// True with an empty list means nothing is trusted yet, which is what an absent file and a blank one
    /// both mean: Forget deletes the file, and a blank one is the same intent written by hand. A file from
    /// before several phones could be trusted holds one address, and reads as a list of one.
    /// </summary>
    private bool TryRead(out List<string> trusted)
    {
        trusted = [];
        if (forgetFailed)
        {
            return false;
        }

        try
        {
            var lines = File.ReadAllText(path).Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            trusted = [.. lines.Distinct(StringComparer.OrdinalIgnoreCase)];
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // Genuinely absent. Not File.Exists, which also answers false when the folder cannot be read.
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The trusted phones could not be read from {path}: {e.Message}");
            return false;
        }
    }
}
