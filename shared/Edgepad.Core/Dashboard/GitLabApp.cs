using System.Globalization;

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
