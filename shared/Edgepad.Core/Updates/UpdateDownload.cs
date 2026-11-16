using System.Net;
using System.Security.Cryptography;

namespace Edgepad.Updates;

/// <summary>
/// Fetches release files, and nothing else. https only, from GitHub's own hosts, with every redirect followed by hand
/// so each hop is checked, and a byte cap enforced both on the announced length and on the bytes actually read.
/// Every failure returns null and leaves no partial file; the caller then installs nothing.
/// </summary>
internal static class UpdateDownload
{
    public const long MaxSumsBytes = 64 * 1024;
    public const long MaxSignatureBytes = 1024;
    public const long MaxAssetBytes = 200L * 1024 * 1024;

    private const int MaxRedirects = 5;
    private const int BufferBytes = 81920;
    private const int Percent = 100;
    private static readonly TimeSpan SmallTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AssetTimeout = TimeSpan.FromMinutes(20);

    /// <summary>The body of <paramref name="uri"/>, at most <paramref name="maxBytes"/> long; null on any failure.</summary>
    public static async Task<byte[]?> GetBytesAsync(
        Uri uri, long maxBytes, HttpMessageHandler? handler, CancellationToken ct)
    {
        try
        {
            using var client = NewClient(handler, SmallTimeout);
            using var response = await OpenAsync(client, uri, maxBytes, ct).ConfigureAwait(false);
            await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[BufferBytes];
            int read;
            while ((read = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + read > maxBytes)
                {
                    throw new InvalidDataException("The download is longer than allowed.");
                }

                memory.Write(buffer, 0, read);
            }

            return memory.ToArray();
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            Log.Write($"A release file could not be fetched: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="uri"/> to <paramref name="destPath"/> + ".download" and returns its SHA-256, or null
    /// on any failure (the half-written file is removed). It never renames: the caller moves the file into place
    /// only after the hash matches the signed list.
    /// </summary>
    public static async Task<byte[]?> DownloadAsync(
        Uri uri, string destPath, long maxBytes, IProgress<int>? progress, HttpMessageHandler? handler, CancellationToken ct)
    {
        var temp = destPath + ".download";
        try
        {
            using var client = NewClient(handler, AssetTimeout);
            using var response = await OpenAsync(client, uri, maxBytes, ct).ConfigureAwait(false);
            var announced = response.Content.Headers.ContentLength;
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, BufferBytes, useAsync: true))
            {
                var buffer = new byte[BufferBytes];
                long total = 0;
                var last = -1;
                int read;
                while ((read = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new InvalidDataException("The download is longer than allowed.");
                    }

                    hasher.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    if (announced is > 0)
                    {
                        var percent = (int)(total * Percent / announced.Value);
                        if (percent != last)
                        {
                            last = percent;
                            progress?.Report(Math.Min(percent, Percent));
                        }
                    }
                }
            }

            return hasher.GetHashAndReset();
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            Log.Write($"A release file could not be downloaded: {e.GetType().Name}: {e.Message}");
            Discard(temp);
            return null;
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            Discard(temp);
            throw;
        }
    }

    /// <summary>Whether <paramref name="uri"/> may be fetched: https, no credentials or odd port, on GitHub's own hosts.</summary>
    internal static bool IsAllowed(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && uri.UserInfo.Length == 0
        && (uri.Host == "github.com" || uri.Host.EndsWith(".githubusercontent.com", StringComparison.Ordinal));

    private static HttpClient NewClient(HttpMessageHandler? handler, TimeSpan timeout)
    {
        var client = handler is null
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            : new HttpClient(handler, disposeHandler: false);
        client.Timeout = timeout;
        return client;
    }

    private static async Task<HttpResponseMessage> OpenAsync(HttpClient client, Uri uri, long maxBytes, CancellationToken ct)
    {
        var current = uri;
        for (var redirects = 0; ; redirects++)
        {
            if (!IsAllowed(current))
            {
                throw new InvalidDataException("The address is not an https address on GitHub.");
            }

            var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var keep = false;
            try
            {
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
                    or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var next = response.Headers.Location
                        ?? throw new InvalidDataException("A redirect named no address.");
                    if (redirects >= MaxRedirects)
                    {
                        throw new InvalidDataException("Too many redirects.");
                    }

                    current = next.IsAbsoluteUri ? next : new Uri(current, next);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > maxBytes)
                {
                    throw new InvalidDataException("The download announces more than allowed.");
                }

                keep = true;
                return response;
            }
            finally
            {
                if (!keep)
                {
                    response.Dispose();
                }
            }
        }
    }

    private static void Discard(string temp)
    {
        try
        {
            File.Delete(temp);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The partial download {temp} could not be removed: {e.Message}");
        }
    }
}
