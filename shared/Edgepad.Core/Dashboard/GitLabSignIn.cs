using System.Text.Json;

namespace Edgepad.Dashboard;

/// <summary>
/// gitlab.com sign-in by the OAuth device authorization grant (https://docs.gitlab.com/api/oauth2/): ask for a code with
/// <see cref="GitLabApp.ClientId"/> and the scopes <see cref="GitLabApp.Scope"/>, show it, and poll until the person has entered it at
/// <see cref="GitLabApp.DevicePage"/>. The tokens go straight into the <see cref="ISecretStore"/>; they are never logged, never
/// returned and never sent to the phone. Only gitlab.com is asked, redirects are refused, every reply is capped at 16 KiB and 15 s,
/// and there is no client secret anywhere. One flow at a time: starting another cancels the first. A self-hosted server does not
/// use this: it takes a pasted token (see <see cref="GitLabHost.SaveServerAsync"/>).
/// </summary>
internal sealed class GitLabSignIn(
    ISecretStore store,
    IntegrationSettings settings,
    HttpMessageHandler? handler = null,
    Func<DateTimeOffset>? clock = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private const int MaxLifeSeconds = 31 * 24 * 3600;
    private const string GrantType = "urn:ietf:params:oauth:grant-type:device_code";

    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly DeviceFlow flow = new(
        handler, clock ?? (() => DateTimeOffset.UtcNow), delay ?? ((span, token) => Task.Delay(span, token)));

    /// <summary>Whether a usable token is saved (from this sign-in or pasted for a server).</summary>
    public bool IsSignedIn => GitTokens.IsToken(store.Get(GitLabApp.TokenName));

    /// <summary>The form body of the device request: this application and the two read scopes, spaces written as <c>+</c> as GitLab's docs do.</summary>
    public static string DeviceForm() =>
        $"client_id={Uri.EscapeDataString(GitLabApp.ClientId)}&scope={Uri.EscapeDataString(GitLabApp.Scope).Replace("%20", "+", StringComparison.Ordinal)}";

    /// <summary>Asks gitlab.com for a code. <see cref="SignInState.Failed"/> when it cannot be asked or answers outside the documented ranges.</summary>
    public async Task<DeviceCode> StartAsync(CancellationToken ct)
    {
        Cancel();
        var reply = await flow.PostAsync(GitLabApp.DeviceCodeUri, DeviceForm(), "Asking GitLab for a sign-in code", ct).ConfigureAwait(false);
        if (reply is not { IsSuccess: true } || DeviceFlow.ParseCode(reply.Body) is not { } issued)
        {
            return new DeviceCode(SignInState.Failed, string.Empty, GitLabApp.DevicePage, TimeSpan.Zero);
        }

        // Begin forgets every saved token and the server address, so the tokens this flow saves can never sit beside another server.
        var epoch = GitLabApp.Begin(store, settings);
        if (epoch < 0)
        {
            return new DeviceCode(SignInState.Failed, string.Empty, GitLabApp.DevicePage, TimeSpan.Zero);
        }

        flow.Begin(issued, epoch);
        return new DeviceCode(SignInState.Started, issued.UserCode, GitLabApp.DevicePage, TimeSpan.FromSeconds(issued.ExpiresIn));
    }

    /// <summary>
    /// Polls until the person has entered the code, said no, or the code ran out, honouring GitLab's interval and its slow_down. Never
    /// throws: cancelling <paramref name="ct"/> or calling <see cref="Cancel"/> ends it as <see cref="SignInState.Cancelled"/>.
    /// </summary>
    public Task<SignInState> WaitAsync(CancellationToken ct) => flow.WaitAsync(
        GitLabApp.TokenUri,
        deviceCode => $"client_id={Uri.EscapeDataString(GitLabApp.ClientId)}&device_code={Uri.EscapeDataString(deviceCode)}&grant_type={Uri.EscapeDataString(GrantType)}",
        "Waiting for GitLab sign-in",
        ReadToken,
        Save,
        ct);

    /// <summary>Stops a sign-in in progress; its <see cref="WaitAsync"/> ends as <see cref="SignInState.Cancelled"/>.</summary>
    public void Cancel() => flow.Cancel();

    /// <summary>
    /// A token reply: a refresh token needs an expires_in (1 s..31 days) beside it. A reply without a refresh token is kept as a token
    /// that is used until GitLab refuses it. A value outside the ranges is a failure.
    /// </summary>
    private static DeviceToken? ReadToken(JsonElement root, string token)
    {
        var refresh = GitJson.Text(root, "refresh_token");
        var life = GitJson.Whole(root, "expires_in");
        var fine = GitTokens.IsToken(token) && (refresh is null || (GitTokens.IsToken(refresh) && life is > 0 and <= MaxLifeSeconds));
        return fine ? new DeviceToken(token, refresh, life) : null;
    }

    /// <summary>Saves the tokens for gitlab.com, but only if no server address was saved and no other sign-in began since this flow started; otherwise they are dropped.</summary>
    private bool Save(DeviceToken token, long epoch) =>
        GitLabApp.SaveIfCurrent(store, settings, epoch, null, token.Value, token.Refresh, token.Refresh is null ? null : now() + TimeSpan.FromSeconds(token.ExpiresIn ?? 0));
}
