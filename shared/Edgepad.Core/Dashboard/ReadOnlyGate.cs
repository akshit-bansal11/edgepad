using System.Text.Json;

namespace Edgepad.Dashboard;

/// <summary>A request the <see cref="ReadOnlyGate"/> refused. Thrown, never swallowed: it means the code asked for something it must not.</summary>
internal sealed class ReadOnlyViolation(string message) : InvalidOperationException(message);

/// <summary>
/// Wraps the handler of every git host's HTTP client so that Edgepad can read a host and never change anything on it. It refuses
/// (throws <see cref="ReadOnlyViolation"/>) every request that is not https, and every one that is not GET or HEAD except two
/// kinds of POST: a GraphQL query to <c>https://api.github.com/graphql</c> whose <c>query</c> contains no <c>mutation</c>, and the
/// OAuth device-flow calls to the host's own sign-in endpoints (GitHub's <c>/login/device/code</c> and <c>/login/oauth/access_token</c>,
/// GitLab's <c>/oauth/authorize_device</c> and <c>/oauth/token</c>). The refusal names the host and path only, never the query string.
/// </summary>
internal sealed class ReadOnlyGate(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private static readonly (string Host, string Path)[] DeviceFlow =
    [
        ("github.com", "/login/device/code"),
        ("github.com", "/login/oauth/access_token"),
        ("gitlab.com", "/oauth/authorize_device"),
        ("gitlab.com", "/oauth/token"),
    ];

    private static readonly (string Host, string Path) GraphQl = ("api.github.com", "/graphql");

    /// <summary>Whether this request may go out.</summary>
    public static async Task<bool> AllowsAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is not { IsAbsoluteUri: true } uri || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
        {
            return true;
        }

        if (request.Method != HttpMethod.Post)
        {
            return false;
        }

        var target = (uri.Host.ToLowerInvariant(), uri.AbsolutePath);
        if (Array.Exists(DeviceFlow, flow => flow == target))
        {
            return true;
        }

        return target == GraphQl && request.Content is { } content && IsQuery(await content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!await AllowsAsync(request, cancellationToken).ConfigureAwait(false))
        {
            throw new ReadOnlyViolation($"{request.Method} {request.RequestUri?.Host}{request.RequestUri?.AbsolutePath} is not read only");
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A JSON object whose <c>query</c> string has no <c>mutation</c> in it, in any case. Judged after the JSON is decoded, so an escape cannot hide the word.</summary>
    private static bool IsQuery(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("query", out var query) && query.ValueKind == JsonValueKind.String
                && query.GetString() is { } text && !text.Contains("mutation", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
