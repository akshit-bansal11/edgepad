using System.Diagnostics.CodeAnalysis;

namespace Edgepad.Dashboard;

/// <summary>
/// Decides whether a phone's <c>GitOpen</c> text (<c>host/listRevision/index</c>) may open anything. The phone names a place in a
/// list, never an address: the laptop opens only an https address it fetched itself, for exactly the revision it last sent, at an
/// index that list holds, on the host's own domain. Anything else is refused and the caller drops and counts it.
/// </summary>
internal static class GitOpenGate
{
    private const int MaxWire = 64;
    private const int MaxHost = 16;

    /// <param name="wire">The phone's text.</param>
    /// <param name="hosts">Finds the connected host with this id, or null.</param>
    /// <param name="url">The address to open when this returns true.</param>
    public static bool TryResolve(string? wire, Func<string, IGitHost?> hosts, [NotNullWhen(true)] out Uri? url)
    {
        url = null;
        var parts = (wire ?? string.Empty).Split('/');
        if (wire is not { Length: > 0 and <= MaxWire } || parts.Length != 3 || parts[0].Length is < 1 or > MaxHost
            || !DashWire.TryLong(parts[1], out var rev) || !DashWire.TryInt(parts[2], out var index) || index < 0
            || hosts(parts[0]) is not { } host || host.Page is not { } page || page.Rev != rev || index >= page.Urls.Count)
        {
            return false;
        }

        if (!Uri.TryCreate(page.Urls[index], UriKind.Absolute, out var candidate)
            || candidate.Scheme != Uri.UriSchemeHttps || candidate.UserInfo.Length > 0 || !candidate.IsDefaultPort
            || !string.Equals(candidate.Host, host.Domain, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        url = candidate;
        return true;
    }
}
