using Edgepad.Protocol;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;

namespace Edgepad.Bluetooth;

/// <summary>
/// The Edgepad service as BlueZ sees it: an <c>org.bluez.Profile1</c> object this app exports on the system bus,
/// registered with <c>org.bluez.ProfileManager1.RegisterProfile</c> under the service id the phone looks for.
/// bluetoothd then advertises the service, accepts the RFCOMM connection and hands the connected socket over in
/// <c>NewConnection</c> as a file descriptor. Method signatures are from BlueZ's org.bluez.Profile and
/// org.bluez.ProfileManager docs (RequestDisconnection from the older profile-api.txt, which the .rst dropped).
/// </summary>
internal sealed class BluezProfile(
    Func<string?> bluetoothd,
    Func<string, SafeFileHandle, bool> onConnection,
    Action<string> onDisconnection) : IPathMethodHandler
{
    public const string ServiceName = "org.bluez";
    public const string ObjectPath = "/me/akshitbansal/edgepad";

    private const string ProfileInterface = "org.bluez.Profile1";
    private const string DevicePrefix = "/org/bluez/hci";
    private const string DeviceSegment = "dev_";

    // "AA_BB_CC_DD_EE_FF": six two-digit hex bytes, five separators.
    private const int AddressLength = 17;

    public string Path => ObjectPath;

    public bool HandlesChildPaths => false;

    /// <summary>
    /// The RegisterProfile call. Role server with no Channel lets bluetoothd pick a free RFCOMM channel and
    /// write the SDP record for the id itself. RequireAuthentication is the analogue of Windows'
    /// BluetoothEncryptionWithAuthentication: only a device already paired with this laptop gets a connection.
    /// RequireAuthorization is off because BlueZ's own authorization agent is not the question Edgepad asks:
    /// the trust check in the handshake is, and it fails closed.
    /// </summary>
    public static MessageBuffer RegisterMessage(DBusConnection bus, string destination)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: destination,
            path: "/org/bluez",
            @interface: "org.bluez.ProfileManager1",
            member: "RegisterProfile",
            signature: "osa{sv}");
        writer.WriteObjectPath(ObjectPath);
        writer.WriteString(ProtocolConstants.ServiceId.ToString());
        var options = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("Name");
        writer.WriteVariantString("Edgepad");
        writer.WriteDictionaryEntryStart();
        writer.WriteString("Role");
        writer.WriteVariantString("server");
        writer.WriteDictionaryEntryStart();
        writer.WriteString("RequireAuthentication");
        writer.WriteVariantBool(true);
        writer.WriteDictionaryEntryStart();
        writer.WriteString("RequireAuthorization");
        writer.WriteVariantBool(false);
        writer.WriteDictionaryEnd(options);
        return writer.CreateMessage();
    }

    /// <summary>
    /// The phone's address from its BlueZ device path, <c>/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF</c>, as
    /// <c>AA:BB:CC:DD:EE:FF</c>: upper case, so the trust file reads the same however BlueZ spelled it. Anything
    /// else is refused rather than guessed at, since the address is what trust is granted to.
    /// </summary>
    public static bool TryAddress(string devicePath, out string address)
    {
        address = "";
        if (!devicePath.StartsWith(DevicePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = devicePath.AsSpan(DevicePrefix.Length);
        var slash = rest.IndexOf('/');
        if (slash <= 0 || rest[..slash].ContainsAnyExceptInRange('0', '9'))
        {
            return false;
        }

        var device = rest[(slash + 1)..];
        if (!device.StartsWith(DeviceSegment, StringComparison.Ordinal) || device.Length != DeviceSegment.Length + AddressLength)
        {
            return false;
        }

        var hex = device[DeviceSegment.Length..];
        for (var i = 0; i < hex.Length; i++)
        {
            var separator = i % 3 == 2;
            if (separator ? hex[i] != '_' : !char.IsAsciiHexDigit(hex[i]))
            {
                return false;
            }
        }

        address = hex.ToString().Replace('_', ':').ToUpperInvariant();
        return true;
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        // Top-level boundary of a D-Bus dispatch: nothing may escape it, and every call gets an answer.
        try
        {
            Handle(context);
        }
        catch (Exception e)
        {
            Log.Write($"A BlueZ profile call failed: {e}");
            context.ReplyError("org.bluez.Error.Rejected", "Edgepad could not take the connection");
        }

        return ValueTask.CompletedTask;
    }

    private void Handle(MethodContext context)
    {
        var request = context.Request;
        if (request.InterfaceAsString != ProfileInterface)
        {
            context.ReplyUnknownMethodError();
            return;
        }

        // The object sits on the system bus, where any local process may call it. Only the bluetoothd this
        // profile is registered with may hand over a connection: anyone else could pass a socket of their own
        // with a trusted phone's address on it and type into the owner's session.
        if (bluetoothd() is not { } owner || request.SenderAsString != owner)
        {
            Log.Write($"Refused a {request.MemberAsString} call from {request.SenderAsString}: it is not bluetoothd");
            context.ReplyError("org.freedesktop.DBus.Error.AccessDenied", "Only bluetoothd may call this profile");
            return;
        }

        switch (request.MemberAsString, request.SignatureAsString)
        {
            case ("NewConnection", "oha{sv}"):
                NewConnection(context);
                break;
            case ("RequestDisconnection", "o"):
                // The link went down under bluetoothd; the descriptor is this app's to close.
                if (TryAddress(request.GetBodyReader().ReadObjectPathAsString(), out var address))
                {
                    onDisconnection(address);
                }

                Reply(context);
                break;
            case ("Release", ""):
                // bluetoothd unregistered the profile, which it does as it stops; the server re-registers when
                // the name has a new owner. Release is [noreply], so nothing is answered.
                Log.Write("BlueZ released the Edgepad profile");
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    private void NewConnection(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var path = reader.ReadObjectPathAsString();

        // Taken out of the message, so from here it is this method's to close: on every path below, either the
        // server owns it or it is disposed. fd_properties (Version, Features) mean nothing to this profile.
        var socket = reader.ReadHandle<SafeFileHandle>();
        bool taken;
        try
        {
            taken = TryAddress(path, out var address) ? onConnection(address, socket) : Malformed(path);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        if (!taken)
        {
            socket.Dispose();
            context.ReplyError("org.bluez.Error.Rejected", "Edgepad refused the connection");
            return;
        }

        Reply(context);
    }

    private static bool Malformed(string path)
    {
        Log.Write($"Refused a connection from {path}: not a BlueZ device path");
        return false;
    }

    private static void Reply(MethodContext context)
    {
        using var writer = context.CreateReplyWriter(null);
        context.Reply(writer.CreateMessage());
    }
}
