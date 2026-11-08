using System.Collections.Concurrent;
using Tmds.DBus.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// What a Bluetooth phone calls itself: the Alias BlueZ keeps for its device, which is the name on the phone's own
/// Bluetooth screen. Asked once per phone and remembered. <see cref="Of"/> may wait for the bus deadline, so it is for
/// a session thread, never the UI thread; the UI reads <see cref="Cached"/>.
/// </summary>
internal static class PhoneName
{
    private const int AddressLength = 17;

    private static readonly Bus System = Bus.OnSystem();
    private static readonly ConcurrentDictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records a name a phone gave over its own link (a Wi-Fi or USB phone says its name when it pairs).</summary>
    public static void Remember(string address, string name) => Names[address] = name;

    public static string? Cached(string address) => Names.TryGetValue(address, out var name) ? name : null;

    public static string? Of(string address)
    {
        if (Cached(address) is { } known)
        {
            return known;
        }

        // ponytail: the first adapter only (hci0); a laptop with two Bluetooth adapters falls back to the address.
        if (address.Length != AddressLength || Read(address) is not { } alias || IsAddress(alias, address))
        {
            return null;
        }

        Names[address] = alias;
        return alias;
    }

    /// <summary>BlueZ answers an unnamed device with its address, dashes for colons; that is no name.</summary>
    public static bool IsAddress(string alias, string address) =>
        alias.Replace('-', ':').Equals(address, StringComparison.OrdinalIgnoreCase);

    private static string? Read(string address)
    {
        var path = "/org/bluez/hci0/dev_" + address.Replace(':', '_');
        var read = System.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(
                    destination: "org.bluez",
                    path: path,
                    @interface: "org.freedesktop.DBus.Properties",
                    member: "Get",
                    signature: "ss");
                writer.WriteString("org.bluez.Device1");
                writer.WriteString("Alias");
                return writer.CreateMessage();
            },
            (Message reply, object? _) => reply.GetBodyReader().ReadVariantValue(),
            out var value);

        return read && value is { Type: VariantValueType.String } v ? v.GetString() : null;
    }
}
