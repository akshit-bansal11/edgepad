using System.Diagnostics;
using System.Text;
using Edgepad.Controls;
using Edgepad.Protocol;
using Edgepad.Trust;

namespace Edgepad.Macros;

/// <summary>
/// The launcher's action table, and the only thing that knows what a macro button means.
///
/// The founding rule of this project is that gestures are recognised on the phone and the laptop only
/// executes: the phone sends semantic frames, never an arbitrary key or command line, and the laptop owns
/// the action table. A launcher is where that rule is easiest to break and worst to break. So the phone
/// sends an index and nothing else — <see cref="Run"/> takes an <see cref="int"/>, and there is deliberately
/// no overload taking a path, a URL or a command line. No string that arrives over Bluetooth ever reaches
/// <c>Process</c>. The only way a target gets into this list is the owner typing or browsing to it in
/// the editor dialog, on the laptop, in front of the machine it will run on.
///
/// What that buys, stated honestly, because an overstated invariant is worse than none. It means a macro
/// button cannot be repointed from the phone: a slot runs what the owner put in it or nothing, and the
/// protocol stays semantic, exactly as the refresh-rate dial sends a position in a list rather than a rate
/// in hertz. Do not "simplify" it by letting the phone name what it wants to launch.
///
/// What it does NOT buy is containment of a compromised phone. A paired phone already has the keyboard
/// screen: KEY carries a raw virtual-key code and TEXT kind 3 carries arbitrary characters, both straight
/// to SendInput with no table in between, so anything reachable from a keyboard is reachable from the phone
/// whatever this class does. A paired phone is a trusted input device — a keyboard plugged into this
/// laptop, not a sandboxed client — and the trust boundary is the Bluetooth pairing, not this list.
///
/// The file is plain tab-separated text, like every other data file this project writes: one macro per line,
/// name, target, arguments. Not JSON — a format a human can fix in Notepad is worth more here than one a
/// parser likes.
/// </summary>
internal sealed class MacroStore : IMacroRunner
{
    /// <summary>
    /// The phone's grid. Defined with the dispatcher's seam, because the dispatcher checks a slot against it before
    /// the slot ever reaches this store; why it is fifteen is explained there, and it is tied to
    /// <see cref="MaxNameBytes"/>.
    /// </summary>
    public const int MaxMacros = IMacroRunner.MaxMacros;

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
    private readonly IMacroLauncher launcher;
    private readonly Action<Action> schedule;

    /// <summary>
    /// Replaced whole, never mutated in place, so a Bluetooth thread reading it while the UI thread saves
    /// sees either the old list or the new one and never a half-written one.
    /// </summary>
    private volatile IReadOnlyList<Macro> macros = [];

    /// <summary>
    /// <paramref name="schedule"/> runs a launch somewhere other than the caller. The default is the thread pool;
    /// tests pass <c>work => work()</c> so an assertion can follow <see cref="Run"/> directly.
    /// </summary>
    public MacroStore(string path, IMacroLauncher launcher, Action<Action>? schedule = null)
    {
        this.path = path;
        this.launcher = launcher;
        this.schedule = schedule ?? (work => ThreadPool.UnsafeQueueUserWorkItem(_ => work(), null));
        Reload();
    }

    public static MacroStore ForCurrentUser(IMacroLauncher launcher) => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgepad", "macros.txt"),
        launcher);

    /// <summary>Where the list is kept, for an error message that has to say which file could not be written.</summary>
    public string FilePath => path;

    /// <summary>The macros in order. Index is what the phone names. Never longer than MaxMacros.</summary>
    public IReadOnlyList<Macro> Macros => macros;

    /// <summary>Re-reads from disk, and tells anyone watching what came back.</summary>
    public void Reload()
    {
        lock (gate)
        {
            macros = Read();
        }

        Published();
    }

    /// <summary>
    /// Writes the list, and only then takes it as the current one. Returns why the write failed, or null on
    /// success; on failure the list in memory and the one the phone was told are left as they were, so what the
    /// owner sees in the app is what is on disk. The editor shows the reason and keeps its edits open.
    /// </summary>
    public string? Save(IReadOnlyList<Macro> macros)
    {
        var clean = Clean(macros);
        lock (gate)
        {
            try
            {
                PrivateStorage.Replace(path, temp => File.WriteAllLines(temp, clean.Select(Line)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Throwing here would come out of a button click as an unhandled exception and take the tray
                // app with it, so the reason goes back to the caller to show instead.
                Log.Write($"Macros could not be saved to {path}: {e.Message}");
                return e.Message;
            }

            this.macros = clean;
        }

        Published();
        return null;
    }

    /// <summary>
    /// Reports the label list whenever it changes, starting with what it is now, until the handle is
    /// disposed. A session subscribes instead of asking once at the handshake: the owner adds a macro on
    /// the laptop and expects the button on the phone, not a reason to restart the app. Shaped like
    /// <c>MediaSessions.Watch</c> because it is the same problem.
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
    /// Hands the new list to every watcher, outside the lock and off this thread. A watcher writes to a
    /// Bluetooth socket, and that write blocks; the caller here is the UI thread inside the editor's OK
    /// click, so a phone that has stopped reading would freeze the tray app — menu, editor and all — until
    /// the socket gave up. Releasing the lock alone would not have helped, because it was never the lock
    /// doing the blocking.
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
            var watcher = target;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    watcher(names);
                }
                catch (Exception e)
                {
                    // An escape from a pool thread ends the process. A watcher failing to tell one phone
                    // about one list is not worth the tray app, the link and the user's session.
                    Log.Write($"A macro watcher failed: {e}");
                }
            });
        }
    }

    /// <summary>
    /// Starts Macros[index] and returns at once. False only when the index is out of range. The launch itself runs
    /// off the caller's thread: the caller is the one that reads the phone's frames, and a shell start that takes
    /// a while would hold up every frame behind it.
    /// </summary>
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
        schedule(() =>
        {
            var clock = Stopwatch.StartNew();
            try
            {
                launcher.Launch(macro.Target, macro.Arguments);
            }
            catch (Exception e)
            {
                // A target that has been moved, deleted or renamed since it was added is the ordinary case, and
                // an escape from a pool thread ends the process.
                Log.Write($"Macro {index} ({macro.Name}) could not start {macro.Target}: {e.Message}");
            }

            Log.Write($"Macro {index} launch call returned in {clock.ElapsedMilliseconds} ms");
        });
        return true;
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
    /// One macro as a line of the file. The icon field is left off entirely when there is none, rather than
    /// written as a trailing tab: a list with no icons on it produces the same bytes it did before there
    /// were icons at all, so an upgrade does not rewrite a file it has nothing to add to.
    /// </summary>
    private static string Line(Macro macro) => macro.Icon is null
        ? $"{macro.Name}\t{macro.Target}\t{macro.Arguments}"
        : $"{macro.Name}\t{macro.Target}\t{macro.Arguments}\t{macro.Icon}";

    /// <summary>
    /// The index of the first macro with nothing to open, or -1 when every one has a target. <see cref="Save"/>
    /// drops such a row, so the editor asks with this before saving and points the owner at it, rather than
    /// letting a macro they filled in disappear on the way to the phone.
    /// </summary>
    public static int WithoutTarget(IReadOnlyList<Macro> macros)
    {
        for (var index = 0; index < macros.Count; index++)
        {
            if (Plain(macros[index].Target).Length == 0)
            {
                return index;
            }
        }

        return -1;
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
            var icon = Plain(macro.Icon ?? string.Empty);
            clean.Add(new Macro(
                Name(macro.Name), target, arguments.Length > 0 ? arguments : null, icon.Length > 0 ? icon : null));
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
        // Cut on a whole character: a surrogate pair split down the middle is not a short name but an invalid one.
        var plain = Plain(name.Replace('/', '-'));
        plain = FrameCodec.Truncate(plain, MaxNameBytes).TrimEnd();

        // The editor asks for a name, but a file edited by hand may not carry one, and a button with no label
        // is worse than a dull one.
        return plain.Length > 0 ? plain : "Macro";
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

            read.Add(new Macro(
                fields[0], fields[1], fields.Length > 2 ? fields[2] : null, fields.Length > 3 ? fields[3] : null));
        }

        // Through Clean rather than straight out, so a hand-edited file is held to the same rules as the
        // editor's output — including the cap, so a file with a thousand lines cannot become a thousand macros.
        return Clean(read);
    }
}

/// <summary>
/// One launcher button. Target is anything the shell can open — an executable, a document, a folder, a URL —
/// and Arguments is null when there are none, so a macro with none is not a macro with an empty command line.
/// Icon overrides the picture the phone shows; null means take it from the target, which is what nearly every
/// macro wants and why it defaults rather than having to be passed.
/// </summary>
internal sealed record Macro(string Name, string Target, string? Arguments, string? Icon = null);
