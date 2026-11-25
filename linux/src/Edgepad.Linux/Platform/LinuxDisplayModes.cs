using System.Globalization;
using Tmds.DBus.Protocol;

namespace Edgepad.Platform;

/// <summary>The refresh rates in Hz the primary display accepts at its current resolution, ascending, and the index of the one in use (-1 when unknown).</summary>
internal sealed record DisplayState(IReadOnlyList<int> Rates, int Current)
{
    public static readonly DisplayState None = new([], -1);
}

/// <summary>The primary output as <c>xrandr --query</c> reports it: its name, the mode in force and that mode's rates.</summary>
internal sealed record XrandrOutput(string Name, string Mode, DisplayState State);

/// <summary>Reads <c>xrandr --query</c>. Pure, so it is tested on pasted output.</summary>
internal static class XrandrParse
{
    /// <summary>
    /// The primary connected output, else the first connected one that has a mode in force; null when there is none.
    /// Only the rates of the mode in force are listed, so an interlaced mode (its name ends in "i") or another
    /// resolution never appears and the dial cannot shrink the desktop.
    /// </summary>
    public static XrandrOutput? Parse(string text)
    {
        XrandrOutput? first = null;
        string? name = null;
        var connected = false;
        var primary = false;
        var lines = new List<(string Mode, List<(double Hz, bool Current)> Rates)>();

        XrandrOutput? Finish()
        {
            return connected && name is not null ? Build(name, lines) : null;
        }

        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0 || line.Trim().Length == 0)
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                if (Finish() is { } done)
                {
                    if (primary)
                    {
                        return done;
                    }

                    first ??= done;
                }

                lines.Clear();
                var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                name = words[0] == "Screen" ? null : words[0];
                connected = words.Length > 1 && words[1] == "connected";
                primary = connected && words.Contains("primary");
                continue;
            }

            if (!connected)
            {
                continue;
            }

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var rates = new List<(double, bool)>();
            foreach (var token in tokens.Skip(1))
            {
                if (double.TryParse(token.Trim('*', '+'), NumberStyles.Float, CultureInfo.InvariantCulture, out var hz) && hz > 0)
                {
                    rates.Add((hz, token.Contains('*')));
                }
            }

            lines.Add((tokens[0], rates));
        }

        if (Finish() is { } last)
        {
            return primary ? last : first ?? last;
        }

        return first;
    }

    private static XrandrOutput? Build(string name, List<(string Mode, List<(double Hz, bool Current)> Rates)> lines)
    {
        var now = lines.FirstOrDefault(l => l.Rates.Any(r => r.Current));
        if (now.Mode is null)
        {
            return null;
        }

        var rates = lines.Where(l => l.Mode == now.Mode)
            .SelectMany(l => l.Rates)
            .Select(r => Whole(r.Hz))
            .Distinct()
            .Order()
            .ToList();
        var current = rates.IndexOf(Whole(now.Rates.First(r => r.Current).Hz));
        return new XrandrOutput(name, now.Mode, new DisplayState(rates, current));
    }

    /// <summary>59.94 and 60.00 are both "60" on a dial, as on Windows.</summary>
    internal static int Whole(double hz) => (int)Math.Round(hz, MidpointRounding.AwayFromZero);
}

/// <summary>One mode of a monitor as Mutter lists it.</summary>
internal sealed record MutterMode(string Id, int Width, int Height, double Rate, bool Current, bool Interlaced);

/// <summary>One physical monitor as Mutter lists it: its connector name, such as <c>eDP-1</c>, and every mode it offers.</summary>
internal sealed record MutterMonitor(string Connector, IReadOnlyList<MutterMode> Modes);

/// <summary>One logical monitor, a place on the desktop, and the connectors that show it (more than one when mirrored).</summary>
internal sealed record MutterLogical(int X, int Y, double Scale, uint Transform, bool Primary, IReadOnlyList<string> Connectors);

/// <summary>What <c>org.gnome.Mutter.DisplayConfig.GetCurrentState</c> said, and the part of it a rate switch needs.</summary>
internal sealed record MutterConfig(uint Serial, IReadOnlyList<MutterMonitor> Monitors, IReadOnlyList<MutterLogical> Logicals)
{
    /// <summary>The primary logical monitor's first connector (else the first logical monitor's).</summary>
    public string? PrimaryConnector
    {
        get
        {
            var logical = Logicals.FirstOrDefault(l => l.Primary) ?? (Logicals.Count > 0 ? Logicals[0] : null);
            return logical is { Connectors.Count: > 0 } ? logical.Connectors[0] : null;
        }
    }

    public DisplayState Describe()
    {
        var now = Current(PrimaryConnector);
        if (now is null)
        {
            return DisplayState.None;
        }

        var rates = SameSize(now).Select(m => XrandrParse.Whole(m.Rate)).Distinct().Order().ToList();
        return new DisplayState(rates, rates.IndexOf(XrandrParse.Whole(now.Rate)));
    }

    /// <summary>The id of the mode at the primary's resolution whose rate is <paramref name="rate"/> Hz, else null.</summary>
    public string? ModeFor(string? connector, int rate) => Current(connector) is { } now
        ? SameSize(now).FirstOrDefault(m => XrandrParse.Whole(m.Rate) == rate)?.Id
        : null;

    public string? CurrentId(string connector) => Current(connector)?.Id;

    private MutterMode? Current(string? connector) =>
        Monitors.FirstOrDefault(m => m.Connector == connector)?.Modes.FirstOrDefault(m => m.Current);

    private IEnumerable<MutterMode> SameSize(MutterMode now) => Monitors
        .First(m => m.Modes.Contains(now)).Modes
        .Where(m => m.Width == now.Width && m.Height == now.Height && !m.Interlaced);
}

/// <summary>
/// The refresh rates of the primary display and the switch between them, for the phone's dial: the dial steps through
/// <see cref="Rates"/> by index. X11 sessions use <c>xrandr</c>; a Wayland session asks Mutter (GNOME) over D-Bus,
/// and any other Wayland desktop has no list, so the dial has no travel there. Only the primary output, and only the
/// resolution in force, as on Windows. UNVERIFIED on hardware.
///
/// Nothing runs on a timer: the list is read by <see cref="Refresh"/> when a phone connects and again after each
/// switch. The phone's index is applied on the session's worker, which keeps only the newest (<see cref="Worker"/>).
/// </summary>
internal sealed class LinuxDisplayModes : IDisposable
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(5);
    private const string MutterName = "org.gnome.Mutter.DisplayConfig";
    private const string MutterPath = "/org/gnome/Mutter/DisplayConfig";

    private readonly bool wayland;
    private readonly bool x11;
    private readonly Bus session = Bus.OnSession();
    private readonly Lock gate = new();
    private DisplayState state = DisplayState.None;
    private XrandrOutput? xrandr;
    private MutterConfig? mutter;

    public LinuxDisplayModes(string? waylandDisplay, string? display)
    {
        wayland = !string.IsNullOrEmpty(waylandDisplay);
        x11 = !wayland && !string.IsNullOrEmpty(display);
    }

    /// <summary>The rates in Hz, ascending. Empty when this display cannot be switched.</summary>
    public IReadOnlyList<int> Rates => Volatile.Read(ref state).Rates;

    /// <summary>Index into <see cref="Rates"/> of the rate in use, or -1.</summary>
    public int Current => Volatile.Read(ref state).Current;

    /// <summary>Reads the display again, once. Blocking: call from a worker or the reporter's thread, never the reader.</summary>
    public void Refresh()
    {
        lock (gate)
        {
            if (x11)
            {
                xrandr = Proc.Run("xrandr", ["--query"], ToolTimeout) is { ExitCode: 0 } result ? XrandrParse.Parse(result.Output) : null;
                Volatile.Write(ref state, xrandr?.State ?? DisplayState.None);
            }
            else if (wayland)
            {
                mutter = session.Call(Query, (Message reply, object? _) => Parse(reply), out var read) ? read : null;
                Volatile.Write(ref state, mutter?.Describe() ?? DisplayState.None);
            }
        }
    }

    /// <summary>Switches to the rate at <paramref name="index"/> of the list last read, then reads the display again. False when refused.</summary>
    public bool Apply(int index)
    {
        lock (gate)
        {
            var rates = state.Rates;
            if (index < 0 || index >= rates.Count || index == state.Current)
            {
                return false;
            }

            var rate = rates[index];
            var done = false;
            if (xrandr is { } output)
            {
                done = Proc.Run(
                    "xrandr",
                    ["--output", output.Name, "--mode", output.Mode, "--rate", rate.ToString(CultureInfo.InvariantCulture)],
                    ToolTimeout) is { ExitCode: 0 };
            }
            else if (mutter is { } config && config.ModeFor(config.PrimaryConnector, rate) is not null)
            {
                done = session.Call(c => ApplyMessage(c, config, rate));
            }

            if (done)
            {
                // The panel may take a moment to settle; one read now, so the phone's dial shows what took.
                Refresh();
            }

            return done;
        }
    }

    public void Dispose() => session.Dispose();

    private static MessageBuffer Query(DBusConnection c)
    {
        using var writer = c.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: MutterName, path: MutterPath, @interface: MutterName, member: "GetCurrentState");
        return writer.CreateMessage();
    }

    /// <summary>The reply to GetCurrentState, <c>(ua((ssss)a(siiddada{sv})a{sv})a(iiduba(ssss)a{sv})a{sv})</c>, down to what a switch needs.</summary>
    private static MutterConfig Parse(Message reply)
    {
        var r = reply.GetBodyReader();
        var serial = r.ReadUInt32();

        var monitors = new List<MutterMonitor>();
        var monitorsEnd = r.ReadArrayStart(DBusType.Struct);
        while (r.HasNext(monitorsEnd))
        {
            r.AlignStruct();
            r.AlignStruct();
            var connector = r.ReadString();
            r.ReadString();
            r.ReadString();
            r.ReadString();

            var modes = new List<MutterMode>();
            var modesEnd = r.ReadArrayStart(DBusType.Struct);
            while (r.HasNext(modesEnd))
            {
                r.AlignStruct();
                var id = r.ReadString();
                var width = r.ReadInt32();
                var height = r.ReadInt32();
                var rate = r.ReadDouble();
                r.ReadDouble();
                var scalesEnd = r.ReadArrayStart(DBusType.Double);
                while (r.HasNext(scalesEnd))
                {
                    r.ReadDouble();
                }

                var props = r.ReadDictionaryOfStringToVariantValue();
                modes.Add(new MutterMode(id, width, height, rate, Flag(props, "is-current"), Flag(props, "is-interlaced")));
            }

            r.ReadDictionaryOfStringToVariantValue();
            monitors.Add(new MutterMonitor(connector, modes));
        }

        var logicals = new List<MutterLogical>();
        var logicalsEnd = r.ReadArrayStart(DBusType.Struct);
        while (r.HasNext(logicalsEnd))
        {
            r.AlignStruct();
            var x = r.ReadInt32();
            var y = r.ReadInt32();
            var scale = r.ReadDouble();
            var transform = r.ReadUInt32();
            var primary = r.ReadBool();
            var connectors = new List<string>();
            var attachedEnd = r.ReadArrayStart(DBusType.Struct);
            while (r.HasNext(attachedEnd))
            {
                r.AlignStruct();
                connectors.Add(r.ReadString());
                r.ReadString();
                r.ReadString();
                r.ReadString();
            }

            r.ReadDictionaryOfStringToVariantValue();
            logicals.Add(new MutterLogical(x, y, scale, transform, primary, connectors));
        }

        return new MutterConfig(serial, monitors, logicals);
    }

    private static bool Flag(Dictionary<string, VariantValue> props, string key) =>
        props.TryGetValue(key, out var v) && v.Type == VariantValueType.Bool && v.GetBool();

    /// <summary>
    /// ApplyMonitorsConfig, <c>ua(iiduba(ssa{sv}))a{sv}</c>, method 1 (temporary: nothing is saved, so a restart of the
    /// session goes back to the saved layout). Every logical monitor is sent as it is, the primary's mode swapped.
    /// A synchronous helper because a message writer cannot live across an await (CS4007).
    /// </summary>
    private static MessageBuffer ApplyMessage(DBusConnection c, MutterConfig config, int rate)
    {
        var primary = config.PrimaryConnector;
        using var writer = c.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: MutterName,
            path: MutterPath,
            @interface: MutterName,
            member: "ApplyMonitorsConfig",
            signature: "ua(iiduba(ssa{sv}))a{sv}");
        writer.WriteUInt32(config.Serial);
        writer.WriteUInt32(1);
        var logicals = writer.WriteArrayStart(DBusType.Struct);
        foreach (var logical in config.Logicals)
        {
            writer.WriteStructureStart();
            writer.WriteInt32(logical.X);
            writer.WriteInt32(logical.Y);
            writer.WriteDouble(logical.Scale);
            writer.WriteUInt32(logical.Transform);
            writer.WriteBool(logical.Primary);
            var attached = writer.WriteArrayStart(DBusType.Struct);
            foreach (var connector in logical.Connectors)
            {
                var mode = connector == primary ? config.ModeFor(connector, rate) : config.CurrentId(connector);
                if (mode is null)
                {
                    continue;
                }

                writer.WriteStructureStart();
                writer.WriteString(connector);
                writer.WriteString(mode);
                var props = writer.WriteDictionaryStart();
                writer.WriteDictionaryEnd(props);
            }

            writer.WriteArrayEnd(attached);
        }

        writer.WriteArrayEnd(logicals);
        var extra = writer.WriteDictionaryStart();
        writer.WriteDictionaryEnd(extra);
        return writer.CreateMessage();
    }
}
