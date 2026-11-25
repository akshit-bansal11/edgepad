using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Edgepad.Dashboard;

/// <summary>
/// Exchanges the refresh token of a gitlab.com sign-in for a new access token. The application is not confidential, so no client
/// secret is sent or kept (GitLab's refresh example for a public client sends only <c>client_id</c> and <c>refresh_token</c>:
/// https://docs.gitlab.com/api/oauth2/). Each refresh returns a new refresh token and kills the old one, so refreshes never overlap:
/// a second caller waits for the first one's answer. Only an answer of <c>invalid_grant</c> (the refresh token is spent, revoked or
/// unknown) deletes what is saved; any other failure keeps the tokens for the next try, so a busy server never signs anyone out.
/// </summary>
internal sealed class GitLabRenewal(ISecretStore store, IntegrationSettings settings, HttpMessageHandler? handler, Func<DateTimeOffset> now)
{
    private const int MaxBytes = 16 * 1024;
    private const int MaxLifetimeSeconds = 31 * 24 * 3600;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    // One flight per store, so refreshes never overlap whoever asks.
    private static readonly ConditionalWeakTable<ISecretStore, RefreshFlight> Flights = new();

    private readonly RefreshFlight flight = Flights.GetOrCreateValue(store);

    /// <summary>Whether a refresh token is saved, so a 401 may be answered by a refresh.</summary>
    public bool CanRefresh => GitTokens.IsToken(store.Get(GitLabApp.RefreshName));

    /// <summary>Whether the saved access token is about to run out (or has) and can be refreshed.</summary>
    public bool IsDue() =>
        CanRefresh && RefreshFlight.IsDue(store.Get(GitLabApp.ExpiresName), now());

    /// <summary>Refreshes, or joins the refresh already running. Never takes the caller's cancellation: a stop after the request left would lose the new refresh token, and the 15 s timeout bounds it.</summary>
    public Task<RefreshOutcome> RefreshAsync() => flight.Join(ExchangeAsync);

    private async Task<RefreshOutcome> ExchangeAsync()
    {
        var epoch = GitLabApp.EpochOf(store);
        var refresh = store.Get(GitLabApp.RefreshName);
        if (!GitTokens.IsToken(refresh))
        {
            GitLabApp.ForgetIfCurrent(store, epoch, refresh);
            return RefreshOutcome.Rejected;
        }

        var form = $"client_id={Uri.EscapeDataString(GitLabApp.ClientId)}&grant_type=refresh_token&refresh_token={Uri.EscapeDataString(refresh)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, GitLabApp.TokenUri)
        {
            Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("Edgepad");
        var reply = await CappedHttp.SendAsync(request, MaxBytes, Timeout, handler, "Renewing the GitLab sign-in", CancellationToken.None).ConfigureAwait(false);
        if (reply is null)
        {
            return RefreshOutcome.Unavailable;
        }

        try
        {
            using var doc = JsonDocument.Parse(reply.Body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return RefreshOutcome.Unavailable;
            }

            if (GitJson.Text(root, "error") is { } error)
            {
                // Only a refresh token GitLab no longer knows ends the sign-in; a busy server or a misunderstanding keeps it.
                if (error != "invalid_grant")
                {
                    return RefreshOutcome.Unavailable;
                }

                GitLabApp.ForgetIfCurrent(store, epoch, refresh);
                return RefreshOutcome.Rejected;
            }

            var access = GitJson.Text(root, "access_token");
            var next = GitJson.Text(root, "refresh_token");
            var life = GitJson.Whole(root, "expires_in");
            return reply.IsSuccess && GitTokens.IsToken(access) && GitTokens.IsToken(next) && life is > 0 and <= MaxLifetimeSeconds
                && GitLabApp.SaveIfCurrent(store, settings, epoch, refresh, access, next, now() + TimeSpan.FromSeconds(life))
                    ? RefreshOutcome.Refreshed
                    : RefreshOutcome.Unavailable;
        }
        catch (JsonException)
        {
            return RefreshOutcome.Unavailable;
        }
    }
}
