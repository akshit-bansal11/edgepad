using System.Globalization;
using System.Runtime.CompilerServices;

namespace Edgepad.Dashboard;

/// <summary>The gitlab.com OAuth application Edgepad signs in through, and the names its tokens are saved under.</summary>
internal static class GitLabApp
{
    /// <summary>
    /// The Application ID of the "Edgepad" application on gitlab.com: not confidential, device authorization grant on, scopes read_api
    /// and read_user only. An Application ID is public by design; no client secret is used or kept anywhere.
    /// </summary>
    public const string ClientId = "90603e45237215e265b1cc5a873ec796353c5e3185c14be0341a371a0dc5d000";

    /// <summary>The two scopes asked for, space separated. Both are read only.</summary>
    public const string Scope = "read_api read_user";

    /// <summary>The access token. A self-hosted server's pasted token is kept under the same name by <see cref="GitTokens.TokenName"/>.</summary>
    public const string TokenName = "gitlab.token";

    public const string RefreshName = "gitlab.refresh";

    /// <summary>When the access token expires, Unix seconds (absent when it does not expire).</summary>
    public const string ExpiresName = "gitlab.expires";

    /// <summary>The Application ID that issued the saved token.</summary>
    public const string ClientName = "gitlab.client";

    /// <summary>The page the person opens to type the code; always this constant, never an address a server sent.</summary>
    public static readonly Uri DevicePage = new("https://gitlab.com/oauth/device");

    public static readonly Uri DeviceCodeUri = new("https://gitlab.com/oauth/authorize_device");
    public static readonly Uri TokenUri = new("https://gitlab.com/oauth/token");

    /// <summary>The API root of gitlab.com.</summary>
    public static readonly Uri Api = new("https://gitlab.com/api/v4/");

    private sealed class State
    {
        public Lock Gate { get; } = new();

        public long Epoch { get; set; }
    }

    // One per secret store, shared by the host, the sign-in and the renewal, so no two of them write tokens at once.
    private static readonly ConditionalWeakTable<ISecretStore, State> States = new();

    /// <summary>The lock every write of a gitlab.com token and every write of the server address holds.</summary>
    public static Lock LockFor(ISecretStore store) => States.GetOrCreateValue(store).Gate;

    /// <summary>The sign-in generation: moved by every sign-in start, server save and sign-out, so a result that began earlier is known to be stale.</summary>
    public static long EpochOf(ISecretStore store)
    {
        var state = States.GetOrCreateValue(store);
        lock (state.Gate)
        {
            return state.Epoch;
        }
    }

    /// <summary>Moves the generation on; whatever began before is dropped when it finishes.</summary>
    public static long Bump(ISecretStore store)
    {
        var state = States.GetOrCreateValue(store);
        lock (state.Gate)
        {
            return ++state.Epoch;
        }
    }

    /// <summary>Starts a gitlab.com sign-in: forgets every saved token and the server address (a token for one server must never sit beside another), then returns the new generation; -1 when the address could not be cleared.</summary>
    public static long Begin(ISecretStore store, IntegrationSettings settings)
    {
        var state = States.GetOrCreateValue(store);
        lock (state.Gate)
        {
            Forget(store);
            return settings.Set(GitTokens.UrlKey("gitlab"), string.Empty) ? ++state.Epoch : -1;
        }
    }

    /// <summary>
    /// Saves a gitlab.com token only if nothing has moved since it was asked for: the generation is still <paramref name="epoch"/>, no
    /// server address is saved and, for a renewal, the saved refresh token is still <paramref name="sentRefresh"/>. Otherwise the
    /// result is dropped, so a gitlab.com token can never land beside a self-hosted address.
    /// </summary>
    public static bool SaveIfCurrent(ISecretStore store, IntegrationSettings settings, long epoch, string? sentRefresh, string access, string? refresh, DateTimeOffset? expires)
    {
        var state = States.GetOrCreateValue(store);
        lock (state.Gate)
        {
            return state.Epoch == epoch && settings.Text(GitTokens.UrlKey("gitlab")).Length == 0
                && (sentRefresh is null || store.Get(RefreshName) == sentRefresh)
                && Save(store, ClientId, access, refresh, expires);
        }
    }

    /// <summary>Forgets everything saved, but only while nothing has moved since <paramref name="epoch"/> and the refresh token is still the one sent.</summary>
    public static void ForgetIfCurrent(ISecretStore store, long epoch, string? sentRefresh)
    {
        var state = States.GetOrCreateValue(store);
        lock (state.Gate)
        {
            if (state.Epoch == epoch && store.Get(RefreshName) == sentRefresh)
            {
                Forget(store);
            }
        }
    }

    /// <summary>
    /// Saves a token with its record; false when it could not be saved (nothing half-saved stays). The order is refresh token, access
    /// token, expiry, then the record, so a stop at any point leaves a pair that works or one that refreshes.
    /// </summary>
    public static bool Save(ISecretStore store, string clientId, string access, string? refresh, DateTimeOffset? expires)
    {
        bool saved;
        if (refresh is not null && expires is { } at)
        {
            saved = store.Set(RefreshName, refresh) && store.Set(TokenName, access)
                && store.Set(ExpiresName, at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            saved = store.Set(TokenName, access);
            if (saved)
            {
                ForgetRenewal(store);
            }
        }

        if (!saved || !store.Set(ClientName, clientId))
        {
            Forget(store);
            return false;
        }

        return true;
    }

    /// <summary>Deletes the refresh token, the expiry and the record: what a pasted token must not sit beside.</summary>
    public static void ForgetRenewal(ISecretStore store)
    {
        store.Delete(RefreshName);
        store.Delete(ExpiresName);
        store.Delete(ClientName);
    }

    /// <summary>Deletes the token and everything that belongs to it.</summary>
    public static void Forget(ISecretStore store)
    {
        store.Delete(TokenName);
        ForgetRenewal(store);
    }
}
