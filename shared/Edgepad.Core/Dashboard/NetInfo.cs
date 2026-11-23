using System.Globalization;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// TEXT NET_INFO: how this laptop is connected. <see cref="Kind"/> is a <see cref="NetKind"/> token, <see cref="Band"/> is
/// <c>2.4</c>, <c>5</c>, <c>6</c> or <c>?</c>, the numbers are -1 when unknown (Windows never knows the signal), the addresses are
/// IPv4 text or empty. There is deliberately no field for a network name, and never one for a hardware address.
/// </summary>
internal sealed record NetInfo(string Kind, string Band, int LinkMbps, int SignalPct, string LocalIp, string Gateway)
{
    /// <summary>Nothing known; what a source that threw publishes.</summary>
    public static readonly NetInfo Unknown = new(NetKind.None, "?", -1, -1, string.Empty, string.Empty);
}

/// <summary>Where the laptop's connection facts come from; one implementation per desktop (C8 Windows, C9 Linux, C10 Mac). May throw; the reporter then publishes <see cref="NetInfo.Unknown"/>.</summary>
internal interface INetInfoSource
{
    /// <summary>The connection as it is now. Called about once a second on a worker while the phone shows the Network page, so it must be cheap.</summary>
    NetInfo Read();
}

/// <summary>The text of <see cref="TextKind.NetInfo"/>.</summary>
internal static class NetInfoWire
{
    private const int MaxIp = 15;

    public static string Encode(NetInfo info) => string.Join(
        '|',
        info.Kind is NetKind.Wifi or NetKind.Ethernet or NetKind.Cellular or NetKind.Other or NetKind.None ? info.Kind : NetKind.Other,
        info.Band is "2.4" or "5" or "6" ? info.Band : "?",
        Number(info.LinkMbps, int.MaxValue),
        Number(info.SignalPct, 100),
        DashWire.Clean(info.LocalIp, MaxIp),
        DashWire.Clean(info.Gateway, MaxIp));

    /// <summary>The record for a text, or null when it is not six fields with a known kind and numbers. For tests; the phone has its own parser.</summary>
    public static NetInfo? Parse(string text)
    {
        var f = text.Split('|');
        if (f.Length != 6
            || f[0] is not (NetKind.Wifi or NetKind.Ethernet or NetKind.Cellular or NetKind.Other or NetKind.None)
            || f[1] is not ("2.4" or "5" or "6" or "?")
            || !int.TryParse(f[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var link)
            || !int.TryParse(f[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var signal))
        {
            return null;
        }

        return new NetInfo(f[0], f[1], link, signal, f[4], f[5]);
    }

    private static string Number(int value, int max) =>
        (value < 0 ? -1 : Math.Min(value, max)).ToString(CultureInfo.InvariantCulture);
}
