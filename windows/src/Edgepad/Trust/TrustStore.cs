namespace Edgepad.Trust;

/// <summary>
/// Trust on first use. Pairing already limits connections to bonded devices; this narrows it to the one
/// phone that connected first, so some other paired device cannot drive the laptop. The tray menu's
/// Forget clears it, and the next phone to connect becomes the trusted one.
///
/// Every file operation here is guarded, and the guards are not all the same. Reading is the one that
/// matters: a file that is there and cannot be read is <em>not</em> the same as no file, and treating it
/// as one would re-arm trust on first use and hand this laptop to whichever phone happened to connect
/// while the file was locked. So a failed read refuses, and only a genuinely absent file admits.
/// </summary>
internal sealed class TrustStore(string path)
{
    private readonly Lock gate = new();

    // The phone admitted when its address could not be written down. Kept for this run so a failed save
    // narrows trust to that phone rather than leaving it open to whichever bonded device connects next.
    private string? unsaved;

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

    /// <summary>True when <paramref name="address"/> may connect. The first address ever offered is remembered.</summary>
    public bool Admit(string address)
    {
        lock (gate)
        {
            if (!TryRead(out var trusted))
            {
                // Fail closed. The phone sees a refusal, the log says why, and Forget in the tray menu is
                // the way out; the alternative is a laptop that quietly re-pairs itself whenever its own
                // trust file is unreadable, which is the single thing this class exists to prevent.
                return false;
            }

            trusted ??= unsaved;
            if (trusted is not null)
            {
                return string.Equals(trusted, address, StringComparison.OrdinalIgnoreCase);
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
    /// True when <paramref name="address"/> is certain to be refused: another phone is trusted, or the trust
    /// file cannot be read. Checked before a new connection may displace the current one; unlike
    /// <see cref="Admit"/> it never grants trust.
    /// </summary>
    public bool Refuses(string address)
    {
        lock (gate)
        {
            if (!TryRead(out var trusted))
            {
                return true;
            }

            trusted ??= unsaved;
            return trusted is not null && !string.Equals(trusted, address, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Forget()
    {
        lock (gate)
        {
            unsaved = null;
            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // This runs from a click on a tray menu item, where an unhandled exception ends the
                // process. The menu item failing to do its one job is not worth the app.
                Log.Write($"The trusted phone could not be forgotten at {path}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// False when the file is there and could not be read — the caller must then refuse rather than guess.
    /// True with a null address means nothing is trusted yet, which is what an absent file and a blank one
    /// both mean: Forget deletes the file, and a blank one is the same intent written by hand.
    /// </summary>
    private bool TryRead(out string? trusted)
    {
        trusted = null;
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
