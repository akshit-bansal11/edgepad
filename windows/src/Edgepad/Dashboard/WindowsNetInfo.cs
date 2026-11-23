using System.Net.NetworkInformation;
using System.Net.Sockets;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// How this PC is connected, from <see cref="NetworkInterface"/> only. The Wi-Fi API (<c>WlanQueryInterface</c>) answers
/// access-denied until the user grants location access, which Edgepad never asks for, so the signal and the band stay unknown
/// (-1 and <c>?</c>) and no network name is ever read. Called about once a second while the phone shows the Network page and
/// never otherwise; the answer is kept for <see cref="EveryMs"/> so the adapter list is not walked each time.
/// </summary>
internal sealed class WindowsNetInfo : INetInfoSource
{
    private const long EveryMs = 4000;

    private readonly Lock gate = new();
    private NetInfo last = NetInfo.Unknown;
    private long at = long.MinValue;

    public NetInfo Read()
    {
        lock (gate)
        {
            var now = Environment.TickCount64;
            if (now - at < EveryMs)
            {
                return last;
            }

            at = now;
            last = Pick();
            return last;
        }
    }

    /// <summary>The <see cref="NetKind"/> of an adapter type.</summary>
    internal static string KindOf(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => NetKind.Wifi,
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => NetKind.Ethernet,
        NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => NetKind.Cellular,
        _ => NetKind.Other,
    };

    /// <summary>Link speed in whole Mbps from bits per second; -1 when the adapter reports none.</summary>
    internal static int Mbps(long bitsPerSecond) =>
        bitsPerSecond <= 0 ? -1 : (int)Math.Min(int.MaxValue, bitsPerSecond / 1_000_000);

    private static NetInfo Pick()
    {
        NetworkInterface? best = null;
        string gateway = string.Empty;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!WindowsStats.Counts(nic.OperationalStatus, nic.NetworkInterfaceType, nic.Name, nic.Description))
            {
                continue;
            }

            var gw = WindowsStats.PickGateway(nic.GetIPProperties().GatewayAddresses.Select(g => g.Address));
            if (gw is not null)
            {
                best = nic;
                gateway = gw.ToString();
                break;
            }

            best ??= nic;
        }

        if (best is null)
        {
            return NetInfo.Unknown;
        }

        var ip = best.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? string.Empty;
        return new NetInfo(KindOf(best.NetworkInterfaceType), "?", Mbps(best.Speed), -1, ip, gateway);
    }
}
