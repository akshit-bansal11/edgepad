using System.Globalization;
using System.Security.Cryptography;
using Edgepad.Protocol;

namespace Edgepad.Trust;

/// <summary>What <see cref="PinStore.Check"/> made of one answer.</summary>
internal abstract record PinResult
{
    private PinResult()
    {
    }

    public sealed record Accepted : PinResult;

    /// <summary>Wrong, with this many tries left before the phone is locked out.</summary>
    public sealed record Wrong(int AttemptsLeft) : PinResult;

    /// <summary>Locked out for this many seconds; the answer was not even looked at.</summary>
    public sealed record Locked(int Seconds) : PinResult;

    /// <summary>
    /// No answer can be judged: the settings cannot be read, there is no PIN, or the PIN or level changed while
    /// this one was being hashed. Never a reason to let the phone in.
    /// </summary>
    public sealed record Unavailable : PinResult;
}

/// <summary>
/// The laptop's security level and the PIN behind levels 2 and 3. Level 1 is trust only, as before there was a PIN;
/// level 2 asks each phone for the PIN once and remembers it in <c>pin-verified.txt</c>; level 3 asks on every
/// connection. The PIN itself is never stored: <c>security.txt</c> holds a salted PBKDF2 hash. That hash does not
/// stop someone who copies the file, since six digits are only 10^6 guesses offline; what stops guessing is the
/// lockout below and the file living in the owner's own profile.
///
/// Reading mirrors <see cref="TrustStore"/>: a settings file that is there and cannot be read, or holds something
/// Edgepad did not write, is not "no PIN". It makes every phone need a PIN that cannot be checked, so every phone is
/// refused until the owner uses Reset PIN. A missing file is level 1 with no PIN, the state before this existed.
///
/// Wrong answers are counted per phone in memory only: quitting the tray app clears them, and doing that already
/// needs the owner's desktop, the very thing the PIN stands in for.
/// </summary>
internal sealed class PinStore(string directory, Func<long>? clock = null, int iterations = PinStore.Iterations)
{
    /// <summary>
    /// PBKDF2-HMAC-SHA256 work factor, from OWASP's Password Storage Cheat Sheet
    /// (https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html, read 2026-10-24:
    /// "PBKDF2-HMAC-SHA256: 600,000 iterations"). Each stored hash carries its own count, so raising this later
    /// re-hashes nothing and breaks nothing; the next Set PIN uses the new figure.
    /// </summary>
    public const int Iterations = 600_000;

    private const string Scheme = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    // The lockout schedule from the protocol: every fifth wrong answer locks the phone out, for 30 s after the first
    // five, doubling with each further five, never more than an hour.
    private const int AttemptsPerBatch = 5;
    private const int FirstLockSeconds = 30;
    private const int MaxLockSeconds = 3600;

    private static readonly char[] LineBreaks = ['\r', '\n'];

    private readonly string securityPath = Path.Combine(directory, "security.txt");
    private readonly string verifiedPath = Path.Combine(directory, "pin-verified.txt");

    private readonly Lock gate = new();

    // Milliseconds, from a clock that only moves forward; a test hands in its own.
    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);

    // Per address: wrong answers so far, and until when it is locked out. A success removes the entry.
    private readonly Dictionary<string, (int Count, long Until)> failures = new(StringComparer.OrdinalIgnoreCase);

    // Bumped by every change of PIN or level and every clearing of the verified list. A check hashed against the old
    // settings proves nothing about the new ones, and must not put a phone on a verified list that was just cleared.
    private int generation;

    // Set when the verified list could not be deleted, cleared when a later delete works. Until then no phone counts
    // as verified and none is added, so level 2 asks on every connection like level 3: a list the owner meant to
    // clear must not let a phone skip the PIN. In memory only, as in TrustStore: after a restart the file is read
    // again, which the caller tells the owner.
    private bool verifiedStale;

    /// <summary>The folder holding both files, for a message that has to send the owner there.</summary>
    public string Folder { get; } = directory;

    public static PinStore ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgepad"));

    /// <summary>The security level, 1 to 3. 3 while the settings cannot be read, which is how they are enforced then.</summary>
    public int Level
    {
        get
        {
            lock (gate)
            {
                return TryRead(out var level, out _) ? level : 3;
            }
        }
    }

    /// <summary>True when a PIN is set, and while the settings cannot be read: an unreadable PIN is not an absent one.</summary>
    public bool HasPin
    {
        get
        {
            lock (gate)
            {
                return !TryRead(out _, out var pin) || pin is not null;
            }
        }
    }

    /// <summary>
    /// Sets a new PIN with a new salt and empties the verified list, so every level-2 phone enters the new PIN once:
    /// a changed secret should not be honoured by a proof of the old one. False when it could not be saved, or the
    /// settings cannot be read (Reset is the way out of that). Throws <see cref="ArgumentException"/> for anything
    /// but six digits, with a message that does not repeat them.
    /// </summary>
    public bool SetPin(string sixDigits)
    {
        // Hundreds of milliseconds at the real work factor, so never under the lock: the tray reads Level on the UI
        // thread, and a connection's Check would otherwise queue behind it.
        var hash = Hash(new Pin(sixDigits), iterations);
        lock (gate)
        {
            if (!TryRead(out var level, out _) || !ClearVerified())
            {
                return false;
            }

            generation++;
            return Save(level, hash);
        }
    }

    /// <summary>
    /// Sets the level and empties the verified list. Levels 2 and 3 are refused without a PIN, which would lock
    /// every phone out with no way to answer. False when refused or not saved.
    /// </summary>
    public bool SetLevel(int level)
    {
        if (level is < 1 or > 3)
        {
            return false;
        }

        lock (gate)
        {
            if (!TryRead(out _, out var pin) || (level > 1 && pin is null) || !ClearVerified())
            {
                return false;
            }

            generation++;
            return Save(level, pin);
        }
    }

    /// <summary>
    /// Whether <paramref name="address"/> must answer the PIN before it is admitted. Always true while the settings
    /// cannot be read.
    /// </summary>
    public bool Required(string address)
    {
        lock (gate)
        {
            if (!TryRead(out var level, out _))
            {
                return true;
            }

            return level switch
            {
                1 => false,
                2 => !ReadVerified().Contains(address, StringComparer.OrdinalIgnoreCase),
                _ => true,
            };
        }
    }

    /// <summary>How much longer <paramref name="address"/> is locked out, or null when it is not.</summary>
    public TimeSpan? LockedFor(string address)
    {
        lock (gate)
        {
            return LockedMs(address) is { } ms ? TimeSpan.FromMilliseconds(ms) : null;
        }
    }

    /// <summary>Tries left before the next lockout; 0 while locked out.</summary>
    public int AttemptsLeft(string address)
    {
        lock (gate)
        {
            if (LockedMs(address) is not null)
            {
                return 0;
            }

            return AttemptsPerBatch - (failures.GetValueOrDefault(address).Count % AttemptsPerBatch);
        }
    }

    /// <summary>
    /// Judges one answer from <paramref name="address"/>. Right: its failures are forgotten and, at level 2, it is
    /// added to the verified list. Wrong: counted, and every fifth one locks it out.
    /// </summary>
    public PinResult Check(string address, Pin pin)
    {
        string stored;
        int level;
        int checkedIn;
        lock (gate)
        {
            if (!TryRead(out level, out var hash) || hash is null)
            {
                return new PinResult.Unavailable();
            }

            if (LockedMs(address) is { } ms)
            {
                return new PinResult.Locked(Seconds(ms));
            }

            stored = hash;
            checkedIn = generation;
        }

        // Outside the lock for the same reason as in SetPin.
        var right = Matches(stored, pin);

        lock (gate)
        {
            if (generation != checkedIn)
            {
                return new PinResult.Unavailable();
            }

            // Another connection from the same address may have used up the attempts while this one hashed.
            if (LockedMs(address) is { } ms)
            {
                return new PinResult.Locked(Seconds(ms));
            }

            if (right)
            {
                failures.Remove(address);
                if (level == 2)
                {
                    Remember(address);
                }

                return new PinResult.Accepted();
            }

            var count = failures.GetValueOrDefault(address).Count + 1;
            if (count % AttemptsPerBatch != 0)
            {
                failures[address] = (count, 0);
                return new PinResult.Wrong(AttemptsPerBatch - (count % AttemptsPerBatch));
            }

            var seconds = LockSeconds(count / AttemptsPerBatch);
            failures[address] = (count, now() + (seconds * 1000L));
            return new PinResult.Locked(seconds);
        }
    }

    /// <summary>
    /// Empties the verified list and keeps the level and the PIN, for Forget trusted phones: a forgotten phone that
    /// had entered the PIN must not get back in at level 2 on one mistaken yes. False when the list could not be
    /// deleted; every phone is then asked for the PIN on every connection until a later clear works.
    /// </summary>
    public bool ForgetVerified()
    {
        lock (gate)
        {
            generation++;
            return ClearVerified();
        }
    }

    /// <summary>
    /// Forgets the PIN and every verified phone and goes back to level 1, for an owner who forgot the PIN. The
    /// caller forgets the trusted phones first and calls this only once they are gone, so the way back in is the
    /// owner's own yes on this laptop. False when a file could not be deleted; the old settings then still apply.
    /// </summary>
    public bool Reset()
    {
        lock (gate)
        {
            generation++;
            failures.Clear();
            var verifiedGone = ClearVerified();
            return Delete(securityPath) && verifiedGone;
        }
    }

    // Called under the lock.
    private bool ClearVerified()
    {
        verifiedStale = !Delete(verifiedPath);
        return !verifiedStale;
    }

    /// <summary>Milliseconds until <paramref name="address"/> may answer again, or null. Called under the lock.</summary>
    private long? LockedMs(string address)
    {
        var at = now();
        return failures.TryGetValue(address, out var f) && f.Until > at ? f.Until - at : null;
    }

    // Rounded up, so a phone told "1 s" is never early.
    private static int Seconds(long ms) => (int)((ms + 999) / 1000);

    // Capped before shifting: 30 << 7 is already past the hour, and a large shift would overflow.
    private static int LockSeconds(int batches) =>
        batches > 7 ? MaxLockSeconds : Math.Min(FirstLockSeconds << (batches - 1), MaxLockSeconds);

    private static string Hash(Pin pin, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin.Digits, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Scheme}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    private static bool Matches(string stored, Pin pin)
    {
        if (!TryParseHash(stored, out var iterations, out var salt, out var expected))
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(pin.Digits, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Splits <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;saltB64&gt;$&lt;hashB64&gt;</c>; false for anything else.</summary>
    private static bool TryParseHash(string stored, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];
        var parts = stored.Split('$');
        if (parts.Length != 4
            || !string.Equals(parts[0], Scheme, StringComparison.Ordinal)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations < 1)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length == SaltBytes && hash.Length == HashBytes;
    }

    /// <summary>
    /// False when <c>security.txt</c> is there and cannot be read, or holds anything Edgepad would not have written
    /// (an unknown line, a level outside 1-3, a malformed hash, level 2 or 3 with no PIN); the caller must then
    /// refuse. True with level 1 and no PIN for a missing file. Called under the lock.
    /// </summary>
    private bool TryRead(out int level, out string? pin)
    {
        level = 1;
        pin = null;
        string text;
        try
        {
            text = File.ReadAllText(securityPath);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The security settings could not be read from {securityPath}, so every phone is refused: {e.Message}");
            return false;
        }

        foreach (var line in text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = line.Split('=', 2, StringSplitOptions.TrimEntries);
            var known = pair.Length == 2 && pair[0] switch
            {
                "level" => int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out level) && level is >= 1 and <= 3,
                "pin" => TryParseHash(pin = pair[1], out _, out _, out _),
                _ => false,
            };
            if (!known)
            {
                return Malformed();
            }
        }

        return level == 1 || pin is not null || Malformed();
    }

    // The file's content is never logged: it holds the hash.
    private bool Malformed()
    {
        Log.Write($"The security settings in {securityPath} are not in a form Edgepad writes, so every phone is refused until the PIN is reset");
        return false;
    }

    /// <summary>
    /// The level-2 phones that have entered the PIN. One that cannot be read, or could not be cleared, is an empty
    /// list: those phones are asked again, which is the safe direction. Called under the lock.
    /// </summary>
    private List<string> ReadVerified()
    {
        if (verifiedStale)
        {
            return [];
        }

        try
        {
            return [.. File.ReadAllText(verifiedPath)
                .Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The PIN-verified phones could not be read from {verifiedPath}: {e.Message}");
            return [];
        }
    }

    // A phone that cannot be written down is simply asked for the PIN again next time. Nothing is added to a list
    // that could not be cleared: written back, it would keep the phones it should have lost. Called under the lock.
    private void Remember(string address)
    {
        if (verifiedStale)
        {
            return;
        }

        var verified = ReadVerified();
        if (!verified.Contains(address, StringComparer.OrdinalIgnoreCase))
        {
            Write(verifiedPath, string.Join('\n', verified.Append(address)) + "\n");
        }
    }

    private bool Save(int level, string? pin) => Write(securityPath, pin is null
        ? string.Create(CultureInfo.InvariantCulture, $"level={level}\n")
        : string.Create(CultureInfo.InvariantCulture, $"level={level}\npin={pin}\n"));

    /// <summary>
    /// Writes beside the file and moves it over, so a crash or a full disk mid-write leaves the old file whole
    /// rather than a half-written one that would read as malformed and refuse every phone.
    /// </summary>
    private static bool Write(string path, string text)
    {
        var temp = path + ".tmp";
        try
        {
            PrivateStorage.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            PrivateStorage.PrepareFile(temp);
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not save {path}: {e.Message}");
            return false;
        }
    }

    private static bool Delete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            // Delete is silent about a missing file and not about a missing folder; nothing is there either way.
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not delete {path}: {e.Message}");
            return false;
        }
    }
}
