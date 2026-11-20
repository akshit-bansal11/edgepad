using System.Net;
using System.Text;

namespace Edgepad.Tests;

/// <summary>A request as the fake server saw it, its body already read.</summary>
internal sealed record SeenRequest(HttpMethod Method, Uri Uri, HttpRequestMessage Message, string Body, string? ContentType)
{
    public string? Header(string name) =>
        Message.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}

/// <summary>A server in memory for the dashboard's HTTP clients: answers with <c>respond</c> and records every request.</summary>
internal sealed class FakeHttp(Func<SeenRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<SeenRequest> Seen { get; } = [];

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent(string.Empty) };

    public static HttpResponseMessage Redirect(string to) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(to) }, Content = new StringContent(string.Empty) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var seen = new SeenRequest(request.Method, request.RequestUri!, request, body, request.Content?.Headers.ContentType?.MediaType);
        Seen.Add(seen);
        return respond(seen);
    }
}

/// <summary>A clock tests move by hand.</summary>
internal sealed class FakeClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Reads the app log as it stands, so a test can show a secret never reached it.</summary>
internal static class LogText
{
    public static string Read()
    {
        try
        {
            using var stream = new FileStream(Log.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return string.Empty;
        }
    }
}
