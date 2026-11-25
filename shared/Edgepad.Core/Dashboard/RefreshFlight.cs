using System.Globalization;

namespace Edgepad.Dashboard;

/// <summary>
/// One token refresh at a time for one saved sign-in. A refresh returns a new refresh token and kills the old one, so
/// two that overlap would leave one of them holding a dead token: a second caller joins the refresh already running
/// and gets its answer instead. <see cref="GitHubTokenRefresh"/> and <see cref="GitLabRenewal"/> each keep one per
/// secret store, in a table of their own, so a GitHub refresh never waits on a GitLab one.
/// </summary>
internal sealed class RefreshFlight
{
    /// <summary>A token this close to its end is refreshed before it is used.</summary>
    internal static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private readonly Lock gate = new();
    private Task<RefreshOutcome>? running;

    /// <summary>
    /// Whether a token whose saved expiry is <paramref name="expires"/>, Unix seconds as text, is within
    /// <see cref="Margin"/> of its end at <paramref name="now"/>, or past it. False for anything unparseable, and for a
    /// time too far out to be a real expiry.
    /// </summary>
    internal static bool IsDue(string? expires, DateTimeOffset now) =>
        long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
        && seconds < DateTimeOffset.MaxValue.ToUnixTimeSeconds() && now + Margin >= DateTimeOffset.FromUnixTimeSeconds(seconds);

    /// <summary>
    /// Runs <paramref name="exchange"/>, or joins the run already under way. The task is stored before the exchange can
    /// finish, so a run that ends at once still clears itself, and the next caller starts a new one.
    /// </summary>
    public Task<RefreshOutcome> Join(Func<Task<RefreshOutcome>> exchange)
    {
        lock (gate)
        {
            return running ??= RunAsync(exchange);
        }
    }

    private async Task<RefreshOutcome> RunAsync(Func<Task<RefreshOutcome>> exchange)
    {
        // Never finishes before Join has stored the task, so the finally below cannot run first.
        await Task.Yield();
        try
        {
            return await exchange().ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                running = null;
            }
        }
    }
}
