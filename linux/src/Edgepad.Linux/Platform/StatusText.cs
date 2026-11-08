namespace Edgepad.Platform;

/// <summary>
/// The status line in the owner's words. The shared handshake reports an address and, for a version mismatch, two
/// protocol numbers; this names the phone and says which half is older, with the fix. The numbers stay in the log.
/// </summary>
internal static class StatusText
{
    private const string Refused = "Refused a phone on protocol ";
    private const string Separator = "; this laptop is on ";

    /// <summary>
    /// <paramref name="status"/> with <paramref name="address"/> shown as <paramref name="display"/> (name and way
    /// in), and a protocol mismatch reworded around <paramref name="name"/>.
    /// </summary>
    public static string Rewrite(string status, string address, string name, string display)
    {
        if (status.StartsWith(Refused, StringComparison.Ordinal))
        {
            var numbers = status[Refused.Length..].Split(Separator);
            if (numbers.Length == 2 && int.TryParse(numbers[0], out var phone) && int.TryParse(numbers[1], out var laptop) && phone != laptop)
            {
                return phone < laptop
                    ? $"{name} could not connect: its Edgepad app is older. Update Edgepad on the phone."
                    : $"{name} could not connect: Edgepad on this laptop is older. Update Edgepad here.";
            }
        }

        return status.Replace(address, display, StringComparison.Ordinal);
    }
}
