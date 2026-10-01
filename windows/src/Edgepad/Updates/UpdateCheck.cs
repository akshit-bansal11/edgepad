namespace Edgepad.Updates;

/// <summary>
/// Whether GitHub holds a newer release than the one running. The twin of the phone's Updates.kt, and asked
/// the same way: the releases/latest page answers with a redirect to the newest tag, so one HEAD request and
/// its Location header say everything, with no JSON to read and no API rate limit to meet. It is the only
/// thing this app ever says to the internet, and it sends nothing about the laptop.
/// </summary>
internal static class UpdateCheck
{
    public const string LatestUrl = Releases + "/latest";

    private const string Releases = "https://github.com/akshit-bansal11/edgepad/releases";
    private const string TagUrl = Releases + "/tag/v";
    private const int VersionParts = 3;
    private const int MaxPartLength = 4;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static TimeSpan Every { get; } = TimeSpan.FromDays(1);

    /// <summary>The newest release's version; null when GitHub could not be asked or gave no usable answer.</summary>
    public static async Task<Version?> LatestAsync()
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = Timeout };
            using var request = new HttpRequestMessage(HttpMethod.Head, LatestUrl);
            using var response = await client.SendAsync(request);
            return VersionIn(response.Headers.Location?.OriginalString);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Log.Write($"Could not ask GitHub for the newest release: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// The version a redirect <paramref name="location"/> names. Anything but this repository's own tag page
    /// with a plain three-part version is refused: the answer is shown in the tray, and it came from the network.
    /// </summary>
    public static Version? VersionIn(string? location)
    {
        if (location is null || !location.StartsWith(TagUrl, StringComparison.Ordinal))
        {
            return null;
        }

        var tag = location[TagUrl.Length..];
        var parts = tag.Split('.');
        var plain = parts.Length == VersionParts
            && parts.All(part => part.Length is > 0 and <= MaxPartLength && part.All(char.IsAsciiDigit));
        return plain ? Version.Parse(tag) : null;
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is a later release than <paramref name="running"/>. A suffix such
    /// as "-draft.38" is ignored, and a running version that cannot be read never counts as older.
    /// </summary>
    public static bool IsNewer(Version candidate, string running) =>
        Version.TryParse(running.Split('-')[0], out var ours) && candidate > ours;
}
