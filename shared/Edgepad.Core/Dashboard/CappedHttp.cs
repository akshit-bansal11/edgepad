using System.Net;

namespace Edgepad.Dashboard;

/// <summary>A reply read by <see cref="CappedHttp"/>: its status, the first value of each response header, and the whole body.</summary>
internal sealed record CappedReply(HttpStatusCode Status, IReadOnlyDictionary<string, string> Headers, byte[] Body)
{
    public bool IsSuccess => (int)Status is >= 200 and <= 299;

    public string? Header(string name) => Headers.GetValueOrDefault(name);
}

/// <summary>
/// One request to a fixed address the caller built from constants, for the dashboard's weather and GitHub clients. Redirects
/// are never followed (a 3xx comes back as its status, which no caller accepts), the body is capped both by its announced
/// length and by the bytes actually read, and a deadline bounds the whole exchange. Nothing about the request is logged:
/// its address may carry the user's city and its headers a token.
/// </summary>
internal static class CappedHttp
{
    private const int BufferBytes = 8192;

    /// <summary>The reply, or null when the network failed, the deadline passed or the body was longer than <paramref name="maxBytes"/>. Cancelling <paramref name="ct"/> throws.</summary>
    public static async Task<CappedReply?> SendAsync(
        HttpRequestMessage request, int maxBytes, TimeSpan timeout, HttpMessageHandler? handler, string what, CancellationToken ct)
    {
        try
        {
            using var client = handler is null
                ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                : new HttpClient(handler, disposeHandler: false);
            client.Timeout = timeout;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength > maxBytes)
            {
                throw new InvalidDataException("The reply announces more than allowed.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[BufferBytes];
            int read;
            while ((read = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + read > maxBytes)
                {
                    throw new InvalidDataException("The reply is longer than allowed.");
                }

                memory.Write(buffer, 0, read);
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers)
            {
                headers[header.Key] = header.Value.FirstOrDefault() ?? string.Empty;
            }

            return new CappedReply(response.StatusCode, headers, memory.ToArray());
        }
        catch (Exception e) when (!ct.IsCancellationRequested
            && e is HttpRequestException or TaskCanceledException or InvalidDataException or IOException)
        {
            Log.Write($"{what} failed: {e.GetType().Name}");
            return null;
        }
    }
}
