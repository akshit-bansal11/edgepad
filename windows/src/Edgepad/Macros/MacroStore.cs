using System.Diagnostics;
using System.Text;
using Edgepad.Controls;

namespace Edgepad.Macros;

/// <summary>
/// The launcher's action table, and the only thing that knows what a macro button means.
///
/// The founding rule of this project is that gestures are recognised on the phone and the laptop only
/// executes: the phone sends semantic frames, never an arbitrary key or command line, and the laptop owns
/// the action table. A launcher is where that rule is easiest to break and worst to break. So the phone
/// sends an index and nothing else — <see cref="Run"/> takes an <see cref="int"/>, and there is deliberately
/// no overload taking a path, a URL or a command line. No string that arrives over Bluetooth ever reaches
/// <see cref="Process"/>. The only way a target gets into this list is the owner typing or browsing to it in
/// the editor dialog, on the laptop, in front of the machine it will run on.
///
/// That is what keeps a compromised phone — or a stranger's phone that somehow gets past the handshake —
/// from running anything at all that the owner did not personally add. Do not "simplify" this by letting the
/// phone name what it wants to launch; the index indirection is the entire security model, exactly as it is
/// for the refresh-rate dial, where the phone sends a position in a list rather than a rate in hertz.
///
/// The file is plain tab-separated text, like every other data file this project writes: one macro per line,
/// name, target, arguments. Not JSON — a format a human can fix in Notepad is worth more here than one a
/// parser likes.
/// </summary>
internal sealed class MacroStore
{
    /// <summary>
    /// A 5x3 grid on the phone, which is a page of buttons rather than a start menu. Fifteen is also exactly
    /// what one TEXT frame can name: 15 x <see cref="MaxNameBytes"/> plus 14 separators is 254 of the 255 a
    /// payload holds, so a full list always reaches the phone labelled. The number is not a coincidence and
    /// must not be raised without cutting the name budget to match.
    /// </summary>
    public const int MaxMacros = 15;

    /// <summary>
    /// A name's budget, counted in UTF-8 bytes rather than characters. Bytes are what the frame's single
    /// length byte counts, and sixteen accented or emoji characters weigh far more than sixteen letters, so
    /// a cap in characters would let a legal-looking name overflow the list and silently lose a label. The
    /// editor's name box reads this too, so the owner meets the limit while typing rather than afterwards.
    /// </summary>
    public const int MaxNameBytes = 16;

    /// <summary>A TEXT payload's length is a single byte, so 255 UTF-8 bytes is the hard ceiling.</summary>
    public const int MaxNamesBytes = 255;

    private readonly Lock gate = new();
    private readonly List<Action<string>> watchers = [];
    private readonly string path;

    /// <summary>
    /// Replaced whole, never mutated in place, so a Bluetooth thread reading it while the UI thread saves
    /// sees either the old list or the new one and never a half-written one.
    /// </summary>
    private volatile IReadOnlyList<Macro> macros = [];

    public MacroStore(string path)
    {
        this.path = path;
        Reload();
    }

    public static MacroStore ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgepad", "macros.txt"));

    /// <summary>The macros in order. Index is what the phone names. Never longer than MaxMacros.</summary>
    public IReadOnlyList<Macro> Macros => macros;

    /// <summary>Re-reads from disk.</summary>
    /// <summary>Re-reads from disk, and tells anyone watching what came back.</summary>
    public void Reload()
    {
        lock (gate)
        {
            macros = Read();
        }

        Published();
    }

    public void Save(IReadOnlyList<Macro> macros)
    {
        var clean = Clean(macros);
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                File.WriteAllLines(path, clean.Select(macro => $"{macro.Name}\t{macro.Target}\t{macro.Arguments}"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The list still takes effect for this session; only the next start loses it. Throwing here
                // would come out of a button click as an unhandled exception and take the tray app with it.
                Log.Write($"Macros could not be saved to {path}: {e.Message}");
            }

            this.macros = clean;
        }

        Published();
    }

    /// <summary>
    /// Reports the label list whenever it changes, starting with what it is now, until the handle is
    /// disposed. A session subscribes instead of asking once at the handshake: the owner adds a macro on
    /// the laptop and expects the button on the phone, not a reason to restart the app. Shaped like
    /// <see cref="Controls.MediaSessions.Watch"/> because it is the same problem.
    /// </summary>
    public IDisposable Watch(Action<string> onChange)
    {
        lock (gate)
        {
            watchers.Add(onChange);
        }

        onChange(Names());
        return new Subscription(() =>
        {
            lock (gate)
            {
                watchers.Remove(onChange);
            }
        });
    }

    /// <summary>
    /// Hands the new list to every watcher, outside the lock: a watcher writes to a Bluetooth socket, and
    /// holding a file lock across that would let a stalled link block the editor's OK button.
    /// </summary>
    private void Published()
    {
        Action<string>[] targets;
        lock (gate)
        {
            targets = [.. watchers];
        }

        var names = Names();
        foreach (var target in targets)
        {
            target(names);
        }
    }

    /// <summary>Launches Macros[index]. False when the index is out of range or the launch failed.</summary>
    public bool Run(int index)
    {
        var current = macros;
        if ((uint)index >= (uint)current.Count)
        {
            // A phone with a longer list than this laptop has — an editor open on the laptop, say — is not an
            // error, just a button that does nothing. The receive loop counts it and carries on.
            return false;
        }

        var macro = current[index];
        try
        {
            // UseShellExecute is what makes a document, a folder and an https:// URL work as targets and not
            // only an .exe: Windows applies whatever is registered for them, exactly as a double-click would.
            // Nothing here waits on or watches what it started, so the handle is let go immediately; a null
            // back means the shell handed the target to something already running, such as a browser opening
            // a URL in a window it already had, which is a success with no new process to hold.
            Process.Start(new ProcessStartInfo(macro.Target, macro.Arguments ?? string.Empty)
            {
                UseShellExecute = true,
            })?.Dispose();

            return true;
        }
        catch (Exception e)
        {
            // This runs on the Bluetooth receive thread. A target that has been moved, deleted or renamed
            // since it was added is the ordinary case, and it must not take the connection down with it.
            Log.Write($"Macro {index} ({macro.Name}) could not start {macro.Target}: {e.Message}");
            return false;
        }
    }

    /// <summary>The names, in order, for sending to the phone.</summary>
    public string Names()
    {
        var text = new StringBuilder();
        var bytes = 0;
        foreach (var macro in macros)
        {
            // Whole names only, and the count is in UTF-8 bytes rather than characters: the length byte counts
            // encoded bytes, and a frame that stopped mid-character would be a protocol error, not a short list.
            var size = Encoding.UTF8.GetByteCount(macro.Name) + (text.Length == 0 ? 0 : 1);
            if (bytes + size > MaxNamesBytes)
            {
                break;
            }

            if (text.Length > 0)
            {
                text.Append('/');
            }

            text.Append(macro.Name);
            bytes += size;
        }

        return text.ToString();
    }

    /// <summary>
    /// What will be written and what the phone will be told, from what the editor collected. Everything that
    /// reaches the list goes through here, whether it came from the dialog or from a file edited by hand.
    /// </summary>
    private static List<Macro> Clean(IReadOnlyList<Macro> macros)
    {
        var clean = new List<Macro>(Math.Min(macros.Count, MaxMacros));
        foreach (var macro in macros)
        {
            var target = Plain(macro.Target);
            if (target.Length == 0)
            {
                // A row with nothing to launch would still take an index, and every macro after it would
                // answer to a different button than the one the owner put it on.
                continue;
            }

            var arguments = Plain(macro.Arguments ?? string.Empty);
            clean.Add(new Macro(Name(macro.Name), target, arguments.Length > 0 ? arguments : null));
            if (clean.Count == MaxMacros)
            {
                break;
            }
        }

        return clean;
    }

    /// <summary>
    /// A name the joined list can survive. '/' separates the names in the TEXT frame and a tab separates the
    /// fields on disk, so neither can appear inside one; the phone would read one name as two.
    /// </summary>
    private static string Name(string name)
    {
        var plain = Plain(name.Replace('/', '-'));
        plain = CutToBytes(plain).TrimEnd();

        // The editor asks for a name, but a file edited by hand may not carry one, and a button with no label
        // is worse than a dull one.
        return plain.Length > 0 ? plain : "Macro";
    }

    /// <summary>
    /// The longest prefix of <paramref name="name"/> that fits <see cref="MaxNameBytes"/> UTF-8 bytes, cut on
    /// a whole character. Cutting mid-character would put a broken code unit on the wire, and a surrogate
    /// pair split down the middle is not a short name but an invalid one.
    /// </summary>
    private static string CutToBytes(string name)
    {
        if (Encoding.UTF8.GetByteCount(name) <= MaxNameBytes)
        {
            return name;
        }

        var end = 0;
        var bytes = 0;
        while (end < name.Length)
        {
            var step = char.IsHighSurrogate(name[end]) && end + 1 < name.Length ? 2 : 1;
            var size = Encoding.UTF8.GetByteCount(name.AsSpan(end, step));
            if (bytes + size > MaxNameBytes)
            {
                break;
            }

            bytes += size;
            end += step;
        }

        return name[..end];
    }

    /// <summary>
    /// Whatever would corrupt a tab-separated line — tabs, newlines and the rest of the control characters —
    /// becomes a space. A space rather than nothing, because dropping the tab out of "foo\tbar" would run the
    /// two sides together into a word that was never in either of them; trailing ones come off in the trim.
    /// </summary>
    private static string Plain(string value)
    {
        var plain = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            plain.Append(char.IsControl(character) ? ' ' : character);
        }

        return plain.ToString().Trim();
    }

    private List<Macro> Read()
    {
        string[] lines;
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            lines = File.ReadAllLines(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Macros could not be read from {path}: {e.Message}");
            return [];
        }

        var read = new List<Macro>();
        foreach (var line in lines)
        {
            var fields = line.Split('\t');
            if (fields.Length < 2)
            {
                continue;
            }

            read.Add(new Macro(fields[0], fields[1], fields.Length > 2 ? fields[2] : null));
        }

        // Through Clean rather than straight out, so a hand-edited file is held to the same rules as the
        // editor's output — including the cap, so a file with a thousand lines cannot become a thousand macros.
        return Clean(read);
    }
}

/// <summary>
/// One launcher button. Target is anything the shell can open — an executable, a document, a folder, a URL —
/// and Arguments is null when there are none, so a macro with none is not a macro with an empty command line.
/// </summary>
internal sealed record Macro(string Name, string Target, string? Arguments);
