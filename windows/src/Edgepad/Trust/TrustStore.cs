namespace Edgepad.Trust;

/// <summary>
/// Trust on first use, with the owner's say-so. Pairing already limits connections to bonded devices; this
/// narrows it to one phone, so some other paired device cannot drive the laptop. The first phone to complete
/// the handshake while nothing is trusted is not taken on its word: the owner is asked, on the laptop, and
/// only a yes writes it down. The tray menu's Forget clears it, and the next phone is asked about in turn.
///
/// Every file operation here is guarded, and the guards are not all the same. Reading is the one that
/// matters: a file that is there and cannot be read is <em>not</em> the same as no file, and treating it
/// as one would re-arm trust on first use and hand this laptop to whichever phone happened to connect
/// while the file was locked. So a failed read refuses, and only a genuinely absent file admits.
/// </summary>
internal sealed class TrustStore(string path, Func<long>? clock = null)
{
    /// <summary>
    /// How long a phone the owner just said no to is turned away unasked. Long enough that a phone's automatic
    /// retries cannot put the question straight back under the owner's cursor.
    /// </summary>
    private static readonly TimeSpan QuietAfterDecline = TimeSpan.FromSeconds(30);

    private readonly Lock gate = new();

    // Milliseconds, from a clock that only moves forward; a test hands in its own.
    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);

    // The phone the owner last said no to, and until when it is not asked about again.
    private string? declined;
    private long declinedUntil;

    // The phone admitted when its address could not be written down. Kept for this run so a failed save
    // narrows trust to that phone rather than leaving it open to whichever bonded device connects next.
    private string? unsaved;

    // Set while the owner is being asked about one phone. A second phone arriving then is refused unasked
    // rather than stacking a second question on the first, which the owner could answer for the wrong phone.
    private bool asking;

    // Set when Forget could not delete the file. Until it really is gone every phone is refused, exactly as for
    // a file that cannot be read: the phone is still trusted on disk, and the owner believes it is not.
    private bool forgetFailed;

    public static TrustStore ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgepad", "trusted-phone.txt"));

    /// <summary>The trusted address, or null when there is none or it cannot be read.</summary>
    public string? Trusted
    {
        get
        {
            lock (gate)
            {
                return TryRead(out var trusted) ? trusted ?? unsaved : null;
            }
        }
    }

    /// <summary>
    /// True when <paramref name="address"/> may connect. While nothing is trusted, <paramref name="ask"/> puts
    /// the question to the owner and a yes is remembered; otherwise it is never called. It blocks for as long
    /// as the owner takes, so it runs outside the lock: the tray menu reads <see cref="Trusted"/> on the very
    /// thread that is showing the question, and would otherwise wait on its own answer.
    /// </summary>
    public bool Admit(string address, Func<bool> ask)
    {
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
            // Settled again after the wait: a file edited by hand in that minute is still the one that counts.
            if (Decided(address) is { } settled)
            {
                return settled;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                File.WriteAllText(path, address);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Admitted anyway, and remembered until the app quits. Refusing the owner's own phone
                // because the disk is full would be a worse answer than forgetting it at the next start.
                unsaved = address;
                Log.Write($"The trusted phone could not be saved to {path}: {e.Message}");
            }

            return true;
        }
    }

    /// <summary>
    /// Whether <paramref name="address"/> is admitted without asking: true for the trusted phone, false for any
    /// other and for a trust file that cannot be read, null when nothing is trusted yet. Called under the lock.
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

        trusted ??= unsaved;
        return trusted is null ? null : string.Equals(trusted, address, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the owner said no to <paramref name="address"/> recently. Called under the lock.</summary>
    private bool IsDeclined(string address) =>
        declined is not null && now() < declinedUntil && string.Equals(declined, address, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when <paramref name="address"/> is certain to be refused: another phone is trusted, the trust
    /// file cannot be read, or the owner is being asked about a phone right now. A connection arriving during
    /// the question, or from a phone the owner just declined, is turned away without disturbing the phone
    /// being asked about. Checked before a new connection may displace the current one; unlike
    /// <see cref="Admit"/> it never grants trust.
    /// </summary>
    public bool Refuses(string address)
    {
        lock (gate)
        {
            return asking || IsDeclined(address) || Decided(address) == false;
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
                return !TryRead(out var trusted) || (trusted ?? unsaved) is not null;
            }
        }
    }

    /// <summary>
    /// True when the trust file is gone, deleted or never there. False when the delete failed: the phone is then
    /// still trusted on disk, so every phone is refused, as for an unreadable file, until a later Forget succeeds.
    /// </summary>
    public bool Forget()
    {
        lock (gate)
        {
            unsaved = null;
            declined = null;
            try
            {
                File.Delete(path);
                forgetFailed = false;
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // This runs from a click on a tray menu item, where an unhandled exception ends the
                // process. The menu item failing to do its one job is not worth the app, but pretending it
                // worked would leave a phone trusted that the owner believes is forgotten.
                Log.Write($"The trusted phone could not be forgotten at {path}: {e.Message}");
                forgetFailed = true;
                return false;
            }
        }
    }

    /// <summary>
    /// False when the file is there and could not be read, or a Forget could not delete it — the caller must
    /// then refuse rather than guess.
    /// True with a null address means nothing is trusted yet, which is what an absent file and a blank one
    /// both mean: Forget deletes the file, and a blank one is the same intent written by hand.
    /// </summary>
    private bool TryRead(out string? trusted)
    {
        trusted = null;
        if (forgetFailed)
        {
            return false;
        }

        try
        {
            trusted = File.ReadAllText(path).Trim() is { Length: > 0 } address ? address : null;
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // Genuinely absent. Not File.Exists, which also answers false when the folder cannot be read.
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The trusted phone could not be read from {path}: {e.Message}");
            return false;
        }
    }
}
