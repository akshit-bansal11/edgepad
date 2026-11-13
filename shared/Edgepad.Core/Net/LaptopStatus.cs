using System.Globalization;

namespace Edgepad.Net;

/// <summary>
/// The words the laptop apps use for the connection, so Windows and Linux say the same thing. The canonical copy lives in
/// the UX plan; change it there first.
/// </summary>
internal static class LaptopStatus
{
    public const string Starting = "Starting…";
    public const string Waiting = "Waiting for your phone";

    private const string ConnectedPrefix = "Connected to ";
    private const string CouldNotConnect = " could not connect:";

    /// <summary>"Connected to Pixel · Bluetooth"; <paramref name="way"/> is Bluetooth, Wi-Fi, USB, or "Wi-Fi or USB" when unknown.</summary>
    public static string Connected(string name, string way) => $"{ConnectedPrefix}{name} · {way}";

    /// <summary>The prefix a connected status starts with, for a reader that parses it back.</summary>
    public static string ConnectedStart => ConnectedPrefix;

    /// <summary>
    /// What the owner reads when a phone speaks another protocol. A higher number is a newer app, so each side can tell
    /// which half to update without the wire carrying more than it does.
    /// </summary>
    public static string Mismatch(string name, int phoneVersion, int laptopVersion) => phoneVersion < laptopVersion
        ? $"{name}{CouldNotConnect} its Edgepad app is older. Update Edgepad on the phone."
        : $"{name}{CouldNotConnect} Edgepad on this laptop is older. Update Edgepad here.";

    private const string Refused = "Refused a phone on protocol ";
    private const string Separator = "; this laptop is on ";

    /// <summary>
    /// The shared handshake reports a mismatch as two protocol numbers (the log keeps them); this words it for the
    /// owner, naming the phone as <paramref name="name"/>. Any other status is returned as it is.
    /// </summary>
    public static string Rewrite(string status, string name)
    {
        if (status.StartsWith(Refused, StringComparison.Ordinal))
        {
            var numbers = status[Refused.Length..].Split(Separator);
            if (numbers.Length == 2
                && int.TryParse(numbers[0], NumberStyles.None, CultureInfo.InvariantCulture, out var phone)
                && int.TryParse(numbers[1], NumberStyles.None, CultureInfo.InvariantCulture, out var laptop)
                && phone != laptop)
            {
                return Mismatch(name, phone, laptop);
            }
        }

        return status;
    }

    /// <summary>
    /// True for a status that reports something wrong rather than the plain state: a refusal, a mismatch, a radio that
    /// is off. A tray keeps these on screen longer than the next "waiting" would let it.
    /// </summary>
    public static bool IsProblem(string status) =>
        status.StartsWith("Refused ", StringComparison.Ordinal)
        || status.StartsWith("Bluetooth is off", StringComparison.Ordinal)
        || status.Contains(CouldNotConnect, StringComparison.Ordinal);

    /// <summary>True for the status of a live phone.</summary>
    public static bool IsConnected(string status) => status.StartsWith(ConnectedPrefix, StringComparison.Ordinal);
}
