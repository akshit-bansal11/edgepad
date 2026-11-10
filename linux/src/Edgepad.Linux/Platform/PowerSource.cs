using Edgepad.Power;
using Edgepad.Protocol;
using Tmds.DBus.Protocol;

namespace Edgepad.Platform;

/// <summary>The UPower device properties a battery entry is built from, read off org.freedesktop.UPower.Device.</summary>
internal readonly record struct UPowerDevice(uint Type, double Percentage, uint State, string Model, bool IsPresent, bool PowerSupply);

/// <summary>
/// What the phone's battery strip and volume dial show for this laptop: the batteries UPower knows (the laptop's own,
/// and peripherals such as a Bluetooth mouse or headset), and the default audio output. UPower is asked over the
/// system bus; the output through wpctl, or pactl when wpctl is not installed, as <see cref="Audio"/> does. Every
/// method blocks, so it runs on <see cref="PowerReporter"/>'s timers, never the session's reader, and none throws:
/// a missing UPower or sound server reads as nothing.
/// </summary>
internal sealed class PowerSource(Bus system) : IDisposable
{
    private const string UPower = "org.freedesktop.UPower";
    private const string DeviceInterface = "org.freedesktop.UPower.Device";

    // UPower's device Type values (org.freedesktop.UPower.Device, "Type"). Only the ones mapped below are named.
    private const uint TypeBattery = 2;
    private const uint TypeMouse = 5;
    private const uint TypeKeyboard = 6;
    private const uint TypeHeadset = 17;
    private const uint TypeSpeakers = 18;
    private const uint TypeHeadphones = 19;

    // UPower's State values: 1 charging, 4 fully charged (still on the charger).
    private const uint StateCharging = 1;
    private const uint StateFull = 4;

    private const int MaxDevices = 16;

    // As in Audio: set once wpctl could not be started, for the rest of the run.
    private volatile bool pactlOnly;
    private string? pactlSink;
    private (string Kind, string Name)? pactlOutput;

    public PowerSource()
        : this(Bus.OnSystem())
    {
    }

    /// <summary>
    /// The entries for TEXT BATTERIES, laptop first, then peripherals in UPower's order. Line power, devices not
    /// present, and devices with no percentage (UPower reports 0 for those) are left out.
    /// </summary>
    public static List<BatteryEntry> Entries(IEnumerable<UPowerDevice> devices)
    {
        var laptops = new List<BatteryEntry>();
        var others = new List<BatteryEntry>();
        foreach (var d in devices)
        {
            if (!d.IsPresent || !double.IsFinite(d.Percentage) || d.Percentage <= 0)
            {
                continue;
            }

            var kind = d.Type switch
            {
                TypeBattery when d.PowerSupply => BatteryKind.Laptop,
                TypeMouse => BatteryKind.Mouse,
                TypeKeyboard => BatteryKind.Keyboard,
                TypeHeadset or TypeHeadphones => BatteryKind.Headphones,
                TypeSpeakers => BatteryKind.Speaker,
                0 or 1 => null, // unknown, line power
                _ => BatteryKind.Other,
            };
            if (kind is null)
            {
                continue;
            }

            var entry = new BatteryEntry(
                kind,
                (int)Math.Round(Math.Clamp(d.Percentage, 0, 100), MidpointRounding.AwayFromZero),
                d.State is StateCharging or StateFull,
                d.Model);
            (kind == BatteryKind.Laptop ? laptops : others).Add(entry);
        }

        laptops.AddRange(others);
        return laptops;
    }

    /// <summary>
    /// The <c>key = "value"</c> property lines of <c>wpctl inspect</c> (each may start with <c>*</c>) or of a
    /// <c>pactl list sinks</c> block. Lines of any other shape are skipped.
    /// </summary>
    public static Dictionary<string, string> ParseProperties(string text)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('*').Trim();
            var equals = line.IndexOf(" = \"", StringComparison.Ordinal);
            if (equals <= 0 || !line.EndsWith('"'))
            {
                continue;
            }

            properties[line[..equals]] = line[(equals + 4)..^1];
        }

        return properties;
    }

    /// <summary>The output from <c>wpctl inspect @DEFAULT_AUDIO_SINK@</c>: its <c>node.description</c>. Null without one.</summary>
    public static (string Kind, string Name)? ParseWpctlInspect(string? output)
    {
        if (output is null)
        {
            return null;
        }

        var p = ParseProperties(output);
        return p.TryGetValue("node.description", out var name) && name.Length > 0
            ? (Kind(p, name, p.GetValueOrDefault("node.name")), name)
            : null;
    }

    /// <summary>The block of <c>pactl list sinks</c> whose <c>Name:</c> is <paramref name="sink"/>: its <c>Description:</c>. Null when absent.</summary>
    public static (string Kind, string Name)? ParsePactlSinks(string? output, string? sink)
    {
        if (output is null || string.IsNullOrWhiteSpace(sink))
        {
            return null;
        }

        sink = sink.Trim();
        foreach (var block in output.Split("Sink #", StringSplitOptions.RemoveEmptyEntries))
        {
            string? name = null;
            string? description = null;
            foreach (var raw in block.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("Name: ", StringComparison.Ordinal))
                {
                    name = line["Name: ".Length..].Trim();
                }
                else if (line.StartsWith("Description: ", StringComparison.Ordinal))
                {
                    description = line["Description: ".Length..].Trim();
                }
            }

            if (name == sink && !string.IsNullOrEmpty(description))
            {
                return (Kind(ParseProperties(block), description, name), description);
            }
        }

        return null;
    }

    /// <summary>
    /// A Bluetooth or USB headset reads as headphones (by form factor), an HDMI or DisplayPort sink as a monitor,
    /// anything else as a speaker. PulseAudio spells the key <c>device.form_factor</c>, PipeWire <c>device.form-factor</c>.
    /// </summary>
    private static string Kind(Dictionary<string, string> p, string description, string? nodeName)
    {
        var form = p.GetValueOrDefault("device.form_factor") ?? p.GetValueOrDefault("device.form-factor") ?? string.Empty;
        if (form is "headset" or "headphone" or "hands-free")
        {
            return OutputKind.Headphones;
        }

        var label = $"{description} {nodeName} {p.GetValueOrDefault("device.bus")}";
        return label.Contains("hdmi", StringComparison.OrdinalIgnoreCase) || label.Contains("displayport", StringComparison.OrdinalIgnoreCase)
            ? OutputKind.Monitor
            : OutputKind.Speaker;
    }

    /// <summary>The batteries UPower reports; an empty list when UPower or the system bus is not there.</summary>
    public IReadOnlyList<BatteryEntry> ReadBatteries()
    {
        var listed = system.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(
                    destination: UPower,
                    path: "/org/freedesktop/UPower",
                    @interface: UPower,
                    member: "EnumerateDevices");
                return writer.CreateMessage();
            },
            (Message reply, object? _) => reply.GetBodyReader().ReadArrayOfObjectPath(),
            out var paths);
        if (!listed || paths is null)
        {
            return [];
        }

        var devices = new List<UPowerDevice>();
        foreach (var path in paths.Take(MaxDevices))
        {
            if (Device(path.ToString()) is { } device)
            {
                devices.Add(device);
            }
        }

        return Entries(devices);
    }

    /// <summary>The default output, or null when there is none or no tool to ask.</summary>
    public (string Kind, string Name)? ReadOutput()
    {
        if (!pactlOnly)
        {
            var result = Proc.Run("wpctl", ["inspect", "@DEFAULT_AUDIO_SINK@"]);
            if (result is not null)
            {
                return result.ExitCode == 0 ? ParseWpctlInspect(result.Output) : null;
            }

            pactlOnly = true;
        }

        // get-default-sink is the cheap call made every poll; the sink list is read only when the default changed.
        var sink = Proc.Run("pactl", ["get-default-sink"]);
        if (sink is not { ExitCode: 0 })
        {
            return null;
        }

        var name = sink.Output.Trim();
        if (name != pactlSink)
        {
            var list = Proc.Run("pactl", ["list", "sinks"]);
            pactlOutput = list is { ExitCode: 0 } ? ParsePactlSinks(list.Output, name) : null;
            pactlSink = list is { ExitCode: 0 } ? name : null;
        }

        return pactlOutput;
    }

    public void Dispose() => system.Dispose();

    private UPowerDevice? Device(string path)
    {
        var read = system.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(
                    destination: UPower,
                    path: path,
                    @interface: "org.freedesktop.DBus.Properties",
                    member: "GetAll",
                    signature: "s");
                writer.WriteString(DeviceInterface);
                return writer.CreateMessage();
            },
            (Message reply, object? _) => reply.GetBodyReader().ReadDictionaryOfStringToVariantValue(),
            out var p);
        if (!read || p is null)
        {
            return null;
        }

        return new UPowerDevice(
            p.TryGetValue("Type", out var t) && t.Type == VariantValueType.UInt32 ? t.GetUInt32() : 0,
            p.TryGetValue("Percentage", out var pc) && pc.Type == VariantValueType.Double ? pc.GetDouble() : 0,
            p.TryGetValue("State", out var s) && s.Type == VariantValueType.UInt32 ? s.GetUInt32() : 0,
            p.TryGetValue("Model", out var m) && m.Type == VariantValueType.String ? m.GetString() : string.Empty,
            p.TryGetValue("IsPresent", out var ip) && ip.Type == VariantValueType.Bool && ip.GetBool(),
            p.TryGetValue("PowerSupply", out var ps) && ps.Type == VariantValueType.Bool && ps.GetBool());
    }
}
