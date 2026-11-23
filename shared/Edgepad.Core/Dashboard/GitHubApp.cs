using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Edgepad.Dashboard;

/// <summary>The read-only GitHub App Edgepad signs in through, and the fixed names and addresses that go with it.</summary>
internal static class GitHubApp
{
    /// <summary>
    /// The public Client ID of the "Edgepad Dashboard" GitHub App (slug edgepad-dashboard): device flow on, read-only repository
    /// permissions, no account permissions. A Client ID is public by design; the device flow and the refresh of a device-flow
    /// token need no client secret (GitHub docs, "Refreshing user access tokens"), and none may ever be added. An empty value
    /// reports <see cref="SignInState.NotConfigured"/> and asks nobody.
    /// </summary>
    public const string ClientId = "Iv23lie6SY6AFlHwufmK";

    /// <summary>The <see cref="ISecretStore"/> name the access token is kept under.</summary>
    public const string TokenName = "github.token";

    /// <summary>The refresh token (absent when the app's tokens do not expire).</summary>
    public const string RefreshName = "github.refresh";

    /// <summary>When the access token expires, as Unix seconds (absent when it does not expire).</summary>
    public const string ExpiresName = "github.expires";

    /// <summary>The Client ID that issued the saved token; a token without this record came from the old OAuth app.</summary>
    public const string ClientName = "github.client";

    /// <summary>Set (to "1") when a token from the old OAuth app was deleted; the windows then say "Sign in again". Cleared by the next sign-in.</summary>
    public const string ReSignInName = "github.resign";

    private const int MaxClientId = 64;
    private const int MaxToken = 255;
    private const string TokenPrefix = "ghu_";

    /// <summary>The page the user opens to type the code; always this constant, never an address a server sent.</summary>
    public static readonly Uri DevicePage = new("https://github.com/login/device");

    public static readonly Uri DeviceCodeUri = new("https://github.com/login/device/code");
    public static readonly Uri TokenUri = new("https://github.com/login/oauth/access_token");

    /// <summary>
    /// Where the user chooses which repositories the App may read. The one address besides a host's own lists that a phone can ask the
    /// laptop to open; it is a constant here, never text from the phone.
    /// </summary>
    public static readonly Uri InstallUri = new("https://github.com/apps/edgepad-dashboard/installations/new");

    /// <summary>Whether <paramref name="clientId"/> looks like a real Client ID; the empty placeholder does not.</summary>
    public static bool IsConfigured(string clientId) =>
        clientId.Length is > 0 and <= MaxClientId && clientId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_');

    /// <summary>A token GitHub could have issued: 1..255 ASCII letters, digits and underscores. Anything else is never stored or sent.</summary>
    public static bool IsToken([NotNullWhen(true)] string? token) =>
        token is { Length: > 0 and <= MaxToken } && token.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>
    /// A saved token from the old OAuth app (it does not start with <c>ghu_</c>, which every GitHub App user token does, or the record
    /// names another client) is deleted and the "sign in again" note is left. A <c>ghu_</c> token with no record is one whose save
    /// stopped before the record: it is kept and the record is written. Idempotent.
    /// </summary>
    public static void Migrate(ISecretStore store, string clientId)
    {
        if (store.Get(TokenName) is not { } token)
        {
            return;
        }

        var record = store.Get(ClientName);
        if (token.StartsWith(TokenPrefix, StringComparison.Ordinal) && (record is null || record == clientId))
        {
            if (record is null)
            {
                _ = store.Set(ClientName, clientId);
            }

            return;
        }

        Forget(store);
        _ = store.Set(ReSignInName, "1");
    }

    /// <summary>Whether the old token was deleted and nobody has signed in since.</summary>
    public static bool NeedsReSignIn(ISecretStore store) => store.Get(ReSignInName) == "1" && store.Get(TokenName) is null;

    /// <summary>
    /// Saves a token with its record; false when it could not be saved (nothing half-saved stays). The refresh token goes first, then
    /// the access token, the expiry and the record, so a stop at any point leaves a pair that works or one that refreshes.
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
                store.Delete(RefreshName);
                store.Delete(ExpiresName);
            }
        }

        if (!saved || !store.Set(ClientName, clientId))
        {
            Forget(store);
            return false;
        }

        store.Delete(ReSignInName);
        return true;
    }

    /// <summary>Deletes the token and everything that belongs to it.</summary>
    public static void Forget(ISecretStore store)
    {
        store.Delete(TokenName);
        store.Delete(RefreshName);
        store.Delete(ExpiresName);
        store.Delete(ClientName);
    }
}
