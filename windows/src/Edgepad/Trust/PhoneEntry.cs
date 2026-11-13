using Edgepad.Ui;

namespace Edgepad.Trust;

/// <summary>A trusted phone as the Security window lists it: what to call it, how it connects, and when it last did.</summary>
internal sealed record PhoneEntry(string Address, string Name, string Way, DateTimeOffset? Last = null)
{
    private const string LinkPrefix = "link:";

    /// <summary>
    /// Reads <paramref name="address"/> the way the owner would: a Wi-Fi or USB phone by the name it paired with, a
    /// Bluetooth phone by <paramref name="known"/> (Windows' name for it, or the address when it has none).
    /// </summary>
    public static PhoneEntry Describe(string address, IReadOnlyList<(string Id, string Name)> pairings, Func<string, string> known)
    {
        if (!address.StartsWith(LinkPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return new PhoneEntry(address, known(address), "Bluetooth");
        }

        var id = address[LinkPrefix.Length..];
        var paired = pairings.FirstOrDefault(link => string.Equals(link.Id, id, StringComparison.OrdinalIgnoreCase));
        return new PhoneEntry(address, paired.Name is { Length: > 0 } name ? name : Strings.Phone_Name, "Wi-Fi or USB");
    }

    /// <summary>The pairing id of a Wi-Fi or USB phone, or null for any other.</summary>
    public string? LinkId => Address.StartsWith(LinkPrefix, StringComparison.OrdinalIgnoreCase) ? Address[LinkPrefix.Length..] : null;
}
