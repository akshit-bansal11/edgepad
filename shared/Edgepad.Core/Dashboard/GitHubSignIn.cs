using System.Text.Json;

namespace Edgepad.Dashboard;

/// <summary>Where a sign-in stands.</summary>
public enum SignInState
{
    /// <summary>A code was issued; show it and wait.</summary>
    Started,

    /// <summary>The token is saved.</summary>
    SignedIn,

    /// <summary>No Client ID was given (an empty or malformed one); nobody was asked.</summary>
    NotConfigured,

    /// <summary>The user said no on GitHub.</summary>
    Denied,

    /// <summary>The code ran out before the user entered it.</summary>
    Expired,

    /// <summary>Stopped here, or replaced by a newer sign-in.</summary>
    Cancelled,

    /// <summary>GitHub could not be asked, answered something unusable, or the token could not be saved.</summary>
    Failed,
}

/// <summary>What <see cref="GitHubSignIn.StartAsync"/> gives the window: the code to show and the page to open, only when <see cref="State"/> is <see cref="SignInState.Started"/>.</summary>
internal sealed record DeviceCode(SignInState State, string UserCode, Uri Page, TimeSpan ExpiresIn)
{
    public static DeviceCode Not(SignInState state) => new(state, string.Empty, GitHubApp.DevicePage, TimeSpan.Zero);
}

/// <summary>
/// GitHub sign-in by the device flow of a GitHub App (GitHub docs, "Generating a user access token for a GitHub App"): ask github.com for a code,
/// show it, and poll until the user has entered it at https://github.com/login/device. The token goes straight into the
/// <see cref="ISecretStore"/> under <see cref="GitHubApp.TokenName"/>; it is never logged, never returned, and never sent to
/// the phone. github.com is the only host asked, redirects are refused, every reply is capped at 16 KiB and 15 s, and there
/// is no client secret anywhere. One flow at a time: starting another cancels the first.
/// </summary>
internal sealed class GitHubSignIn(
    ISecretStore store,
    string clientId = GitHubApp.ClientId,
    HttpMessageHandler? handler = null,
    Func<DateTimeOffset>? clock = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private const int MaxLifeSeconds = 31 * 24 * 3600;
    private const string GrantType = "urn:ietf:params:oauth:grant-type:device_code";

    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly DeviceFlow flow = new(
        handler, clock ?? (() => DateTimeOffset.UtcNow), delay ?? ((span, token) => Task.Delay(span, token)));

    /// <summary>Whether a usable token is saved. A token from the old OAuth app is deleted first (see <see cref="GitHubApp.Migrate"/>).</summary>
    public bool IsSignedIn
    {
        get
        {
            GitHubApp.Migrate(store, clientId);
            return GitHubApp.IsToken(store.Get(GitHubApp.TokenName));
        }
    }

    /// <summary>Deletes a token from the old OAuth app now. Each laptop calls it once at start, so the old grant goes even if no window or phone ever asks.</summary>
    public void Migrate() => GitHubApp.Migrate(store, clientId);

    /// <summary>Whether a token from the old OAuth app was deleted and nobody has signed in since: the window says "Sign in again".</summary>
    public bool NeedsReSignIn
    {
        get
        {
            GitHubApp.Migrate(store, clientId);
            return GitHubApp.NeedsReSignIn(store);
        }
    }

    /// <summary>
    /// Asks GitHub for a code. No scope is sent: a GitHub App's token reads what the App's own read-only permissions and its
    /// installations allow. <see cref="SignInState.NotConfigured"/> without a request while the Client ID is the
    /// placeholder; <see cref="SignInState.Failed"/> when GitHub cannot be asked or answers outside the documented ranges.
    /// </summary>
    public async Task<DeviceCode> StartAsync(CancellationToken ct)
    {
        if (!GitHubApp.IsConfigured(clientId))
        {
            return DeviceCode.Not(SignInState.NotConfigured);
        }

        Cancel();
        var reply = await flow.PostAsync(GitHubApp.DeviceCodeUri, $"client_id={Uri.EscapeDataString(clientId)}", "Asking GitHub for a sign-in code", ct).ConfigureAwait(false);
        if (reply is not { IsSuccess: true } || DeviceFlow.ParseCode(reply.Body) is not { } issued)
        {
            return DeviceCode.Not(SignInState.Failed);
        }

        flow.Begin(issued);
        return new DeviceCode(SignInState.Started, issued.UserCode, GitHubApp.DevicePage, TimeSpan.FromSeconds(issued.ExpiresIn));
    }

    /// <summary>
    /// Polls until the user has entered the code, said no, or the code ran out, honouring GitHub's interval and its
    /// slow_down. Never throws: cancelling <paramref name="ct"/> or calling <see cref="Cancel"/> ends it as
    /// <see cref="SignInState.Cancelled"/>. A failed poll is tried again at the next interval until the code runs out.
    /// </summary>
    public Task<SignInState> WaitAsync(CancellationToken ct) => flow.WaitAsync(
        GitHubApp.TokenUri,
        deviceCode => $"client_id={Uri.EscapeDataString(clientId)}&device_code={Uri.EscapeDataString(deviceCode)}&grant_type={Uri.EscapeDataString(GrantType)}",
        "Waiting for GitHub sign-in",
        ReadToken,
        (token, _) => GitHubApp.Save(store, clientId, token.Value, token.Refresh, token.ExpiresIn is int life ? now() + TimeSpan.FromSeconds(life) : null),
        ct);

    /// <summary>Stops a sign-in in progress; its <see cref="WaitAsync"/> ends as <see cref="SignInState.Cancelled"/>.</summary>
    public void Cancel() => flow.Cancel();

    /// <summary>Stops any sign-in and forgets the token.</summary>
    public void SignOut()
    {
        Cancel();
        GitHubApp.Forget(store);
        store.Delete(GitHubApp.ReSignInName);
    }

    /// <summary>
    /// A token reply: an expiring token has a refresh token and expires_in (1 s..31 days); one with neither does not expire. Half of
    /// the pair, or a value outside the ranges, is a failure.
    /// </summary>
    private static DeviceToken? ReadToken(JsonElement root, string token)
    {
        var refresh = GitJson.Text(root, "refresh_token");
        var life = DeviceFlow.Number(root, "expires_in");
        var expiring = refresh is not null || life is not null;
        var fine = GitHubApp.IsToken(token)
            && (!expiring || (GitHubApp.IsToken(refresh) && life is > 0 and <= MaxLifeSeconds));
        return fine ? new DeviceToken(token, refresh, life) : null;
    }
}
