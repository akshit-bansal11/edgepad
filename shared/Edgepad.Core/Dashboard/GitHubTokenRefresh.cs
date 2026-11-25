using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Edgepad.Dashboard;

/// <summary>How a refresh of the access token went.</summary>
internal enum RefreshOutcome
{
    /// <summary>A new access token and refresh token are saved.</summary>
    Refreshed,

    /// <summary>GitHub refused the refresh token, or there is none: everything saved was deleted and the user must sign in again.</summary>
    Rejected,

    /// <summary>GitHub could not be asked or answered something unusable; the saved tokens are kept for the next try.</summary>
    Unavailable,
}

/// <summary>
/// Exchanges the refresh token of a GitHub App user token for a new access token (GitHub docs, "Refreshing user access tokens").
/// A token made by the device flow needs no client secret to be refreshed, so none is sent or kept. Each refresh returns a new
/// refresh token and kills the old one, so refreshes never overlap: a second caller waits for the first one's answer. A token
/// that does not expire (no refresh token saved) is never refreshed.
/// </summary>
internal sealed class GitHubTokenRefresh(ISecretStore store, string clientId, HttpMessageHandler? handler, Func<DateTimeOffset> now)
{
    private const int MaxBytes = 16 * 1024;
    private const int MaxLifetimeSeconds = 31 * 24 * 3600;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    // One flight per store, shared by every GitHubApi on it (the window's login lookup builds its own), so refreshes never overlap.
    private static readonly ConditionalWeakTable<ISecretStore, RefreshFlight> Flights = new();

    private readonly RefreshFlight flight = Flights.GetOrCreateValue(store);

    /// <summary>Whether a refresh token is saved, so a 401 may be answered by a refresh.</summary>
    public bool CanRefresh => GitHubApp.IsToken(store.Get(GitHubApp.RefreshName));

    /// <summary>Whether the saved access token is about to run out (or has) and can be refreshed.</summary>
    public bool IsDue() =>
        CanRefresh && RefreshFlight.IsDue(store.Get(GitHubApp.ExpiresName), now());

    /// <summary>Refreshes, or joins the refresh already running. Never takes the caller's cancellation: a stop after the request left would lose the new refresh token, and the 15 s timeout bounds it.</summary>
    public Task<RefreshOutcome> RefreshAsync() => flight.Join(ExchangeAsync);

    private async Task<RefreshOutcome> ExchangeAsync()
    {
        var refresh = store.Get(GitHubApp.RefreshName);
        if (!GitHubApp.IsToken(refresh))
        {
            GitHubApp.Forget(store);
            return RefreshOutcome.Rejected;
        }

        var form = $"client_id={Uri.EscapeDataString(clientId)}&grant_type=refresh_token&refresh_token={Uri.EscapeDataString(refresh)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, GitHubApp.TokenUri)
        {
            Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("Edgepad");
        var reply = await CappedHttp.SendAsync(request, MaxBytes, Timeout, handler, "Renewing the GitHub sign-in", CancellationToken.None).ConfigureAwait(false);
        if (reply is null)
        {
            return RefreshOutcome.Unavailable;
        }

        if (reply.Status == HttpStatusCode.Unauthorized)
        {
            GitHubApp.Forget(store);
            return RefreshOutcome.Rejected;
        }

        try
        {
            using var doc = JsonDocument.Parse(reply.Body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return RefreshOutcome.Unavailable;
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                // GitHub answers a refused refresh token with 200 and bad_refresh_token. Any other error (a busy server, a
                // misunderstanding) keeps the tokens for the next try.
                if (error.GetString() != "bad_refresh_token")
                {
                    return RefreshOutcome.Unavailable;
                }

                GitHubApp.Forget(store);
                return RefreshOutcome.Rejected;
            }

            var access = Text(root, "access_token");
            var next = Text(root, "refresh_token");
            return reply.IsSuccess && GitHubApp.IsToken(access) && GitHubApp.IsToken(next)
                && root.TryGetProperty("expires_in", out var life) && life.ValueKind == JsonValueKind.Number && life.TryGetInt32(out var seconds)
                && seconds is > 0 and <= MaxLifetimeSeconds
                && GitHubApp.Save(store, clientId, access, next, now() + TimeSpan.FromSeconds(seconds))
                    ? RefreshOutcome.Refreshed
                    : RefreshOutcome.Unavailable;
        }
        catch (JsonException)
        {
            return RefreshOutcome.Unavailable;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
