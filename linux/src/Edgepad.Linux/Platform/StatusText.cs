using Edgepad.Net;

namespace Edgepad.Platform;

/// <summary>
/// The status line in the owner's words. The wording of a protocol mismatch is the shared <see cref="LaptopStatus"/>, so
/// Windows and Linux cannot drift; this adds only what that does not know: the phone's address and how it is shown.
/// </summary>
internal static class StatusText
{
    /// <summary>
    /// <paramref name="status"/> with a mismatch reworded around <paramref name="name"/> and <paramref name="address"/>
    /// shown as <paramref name="display"/> (name and way in).
    /// </summary>
    public static string Rewrite(string status, string address, string name, string display) =>
        LaptopStatus.Rewrite(status, name).Replace(address, display, StringComparison.Ordinal);
}
