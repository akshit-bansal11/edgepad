using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Edgepad.Platform;
using Edgepad.Protocol;
using Tmds.DBus.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// How this Linux laptop is connected, for TEXT NET_INFO. The default route's interface gives the kind, link speed and
/// addresses from <c>/proc</c> and <c>/sys</c>; NetworkManager, when it is running, adds the Wi-Fi signal, band and bitrate
/// from the active access point. Never the network's name: the access point's Ssid property is not read. Without NetworkManager
/// the Wi-Fi signal and band are unknown (-1, <c>?</c>), never an error. The NetworkManager answer is kept three seconds.
/// </summary>
internal sealed class LinuxNetInfo(string proc = "/proc", string sys = "/sys") : INetInfoSource, IDisposable
{
    private const string Nm = "org.freedesktop.NetworkManager";
    private static readonly TimeSpan Keep = TimeSpan.FromSeconds(3);

    private readonly Bus bus = Bus.OnSystem();
    private (string Iface, long At, int Signal, string Band, int Mbps)? wifi;

    public NetInfo Read()
    {
        if (ProcParsers.DefaultRoute(ReadFile(Path.Combine(proc, "net/route")) ?? string.Empty) is not { } route)
        {
            return NetInfo.Unknown;
        }

        var dir = Path.Combine(sys, "class", "net", route.Interface);
        var kind = Kind(route.Interface, Directory.Exists(Path.Combine(dir, "wireless")), ReadFile(Path.Combine(dir, "type")));
        var band = "?";
        var signal = -1;
        var mbps = Mbps(ReadFile(Path.Combine(dir, "speed")));
        if (kind == NetKind.Wifi && ReadWifi(route.Interface) is { } w)
        {
            (signal, band) = (w.Signal, w.Band);
            mbps = w.Mbps > 0 ? w.Mbps : mbps;
        }

        return new NetInfo(kind, band, mbps, signal, LocalIp(route.Interface), route.Gateway.ToString());
    }

    public void Dispose() => bus.Dispose();

    /// <summary>The kind from the interface's sysfs facts: a <c>wireless</c> folder is Wi-Fi, ARPHRD type 1 is Ethernet, <c>ww*</c> is a modem.</summary>
    internal static string Kind(string iface, bool hasWirelessDir, string? type) =>
        hasWirelessDir ? NetKind.Wifi
        : iface.StartsWith("ww", StringComparison.Ordinal) ? NetKind.Cellular
        : type?.Trim() == "1" ? NetKind.Ethernet
        : NetKind.Other;

    /// <summary>The <c>speed</c> file in Mb/s; the kernel prints -1 (or refuses the read) when the link has none.</summary>
    internal static int Mbps(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : -1;

    /// <summary>The band of an access point's frequency in MHz.</summary>
    internal static string Band(uint mhz) => mhz switch
    {
        >= 2400 and < 2500 => "2.4",
        >= 4900 and < 5900 => "5",
        >= 5925 and <= 7125 => "6",
        _ => "?",
    };

    /// <summary>NetworkManager's Strength (a percent) as the wire's signal, clamped to 0 to 100.</summary>
    internal static int Signal(int strength) => Math.Clamp(strength, 0, 100);

    private static MessageBuffer Message(DBusConnection c, string path, string @interface, string member, string signature, params string[] args)
    {
        using var writer = c.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Nm, path: path, @interface: @interface, member: member, signature: signature);
        foreach (var arg in args)
        {
            writer.WriteString(arg);
        }

        return writer.CreateMessage();
    }

    private static string LocalIp(string iface)
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name == iface);
            var address = nic?.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            return address?.Address.ToString() ?? string.Empty;
        }
        catch (Exception e) when (e is NetworkInformationException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static string? ReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private (int Signal, string Band, int Mbps)? ReadWifi(string iface)
    {
        var now = Environment.TickCount64;
        if (wifi is { } cached && cached.Iface == iface && now - cached.At < Keep.TotalMilliseconds)
        {
            return (cached.Signal, cached.Band, cached.Mbps);
        }

        var result = Query(iface);
        wifi = (iface, now, result?.Signal ?? -1, result?.Band ?? "?", result?.Mbps ?? -1);
        return result;
    }

    private (int Signal, string Band, int Mbps)? Query(string iface)
    {
        if (!bus.Call(
                c => Message(c, "/org/freedesktop/NetworkManager", Nm, "GetDeviceByIpIface", "s", iface),
                (Message m, object? _) => m.GetBodyReader().ReadObjectPath().ToString(),
                out var device)
            || device is null
            || Get(device, Nm + ".Device.Wireless", "ActiveAccessPoint")?.GetObjectPath().ToString() is not { Length: > 1 } ap)
        {
            return null;
        }

        var bitrateKbps = Get(device, Nm + ".Device.Wireless", "Bitrate")?.GetUInt32() ?? 0;
        var mhz = Get(ap, Nm + ".AccessPoint", "Frequency")?.GetUInt32() ?? 0;
        var strength = Get(ap, Nm + ".AccessPoint", "Strength")?.GetByte();
        return (strength is { } s ? Signal(s) : -1, Band(mhz), bitrateKbps > 0 ? (int)Math.Min(int.MaxValue, bitrateKbps / 1000) : -1);
    }

    private VariantValue? Get(string path, string iface, string property) =>
        bus.Call(
            c => Message(c, path, "org.freedesktop.DBus.Properties", "Get", "ss", iface, property),
            (Message m, object? _) => m.GetBodyReader().ReadVariantValue(),
            out var value)
            ? value
            : null;
}
