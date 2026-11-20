using System.Diagnostics.CodeAnalysis;

namespace Edgepad.Dashboard;

/// <summary>The GitHub OAuth App Edgepad signs in through, and the fixed names and addresses that go with it.</summary>
internal static class GitHubApp
{
    /// <summary>
    /// The public Client ID of the Edgepad OAuth App, registered 2026-11-20 with Device Flow on and token expiry off,
    /// because nothing here refreshes a token. A Client ID is public by design; the device flow needs no client secret,
    /// and none may ever be added. An empty value reports <see cref="SignInState.NotConfigured"/> and asks nobody.
    /// </summary>
    public const string ClientId = "Ov23li11P3z1LCVz9APf";

    /// <summary>The <see cref="ISecretStore"/> name the token is kept under.</summary>
    public const string TokenName = "github.token";

    /// <summary>Public repositories and notifications: the default.</summary>
    public const string ScopePublic = "public_repo notifications";

    /// <summary>Private repositories too, when the user ticks "Include private repositories".</summary>
    public const string ScopePrivate = "repo notifications";

    private const int MaxClientId = 64;
    private const int MaxToken = 255;

    /// <summary>The page the user opens to type the code; always this constant, never an address a server sent.</summary>
    public static readonly Uri DevicePage = new("https://github.com/login/device");

    public static readonly Uri DeviceCodeUri = new("https://github.com/login/device/code");
    public static readonly Uri TokenUri = new("https://github.com/login/oauth/access_token");

    /// <summary>Whether <paramref name="clientId"/> looks like a real Client ID; the empty placeholder does not.</summary>
    public static bool IsConfigured(string clientId) =>
        clientId.Length is > 0 and <= MaxClientId && clientId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_');

    /// <summary>A token GitHub could have issued: 1..255 ASCII letters, digits and underscores. Anything else is never stored or sent.</summary>
    public static bool IsToken([NotNullWhen(true)] string? token) =>
        token is { Length: > 0 and <= MaxToken } && token.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
