using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>Which developer page a provider belongs to.</summary>
internal enum DevKind
{
    Ai,
    Git,
}

/// <summary>How the laptop gets access to a provider: a device-code sign-in, a pasted read-only token, or a hook plugin.</summary>
internal enum SignIn
{
    Device,
    Token,
    Hook,
}

/// <summary>
/// One AI tool or git host. <see cref="Id"/> is the <see cref="AgentToken"/> of an AI tool and the settings key of both kinds,
/// <see cref="FeatureToken"/> the <see cref="Feature"/> named while it is connected, <see cref="LogoId"/> the logo's resource id.
/// </summary>
internal sealed record DevProvider(string Id, DevKind Kind, string Name, string FeatureToken, bool HasLimits, SignIn Flow, string LogoId);

/// <summary>
/// The one list of developer providers, in display order. No screen hard-codes a tool list. Mirrors the PROVIDER rows of
/// protocol/actions.txt; the Swift and Kotlin sides are held to the same rows by their tests.
/// </summary>
internal static class DevProviders
{
    public static readonly IReadOnlyList<DevProvider> All =
    [
        new("claude", DevKind.Ai, "Claude Code", Feature.Claude, true, SignIn.Hook, "claude"),
        new("codex", DevKind.Ai, "Codex", Feature.Codex, true, SignIn.Hook, "openai"),
        new("gemini", DevKind.Ai, "Gemini CLI", Feature.Gemini, false, SignIn.Hook, "gemini"),
        new("opencode", DevKind.Ai, "OpenCode", Feature.OpenCode, false, SignIn.Hook, "opencode"),
        new("github", DevKind.Git, "GitHub", Feature.GitHub, false, SignIn.Device, "github"),
        new("gitlab", DevKind.Git, "GitLab", Feature.GitLab, false, SignIn.Device, "gitlab"),
        new("gitea", DevKind.Git, "Gitea", Feature.Gitea, false, SignIn.Token, "gitea"),
        new("bitbucket", DevKind.Git, "Bitbucket", Feature.Bitbucket, false, SignIn.Token, "bitbucket"),
        new("gitee", DevKind.Git, "Gitee", Feature.Gitee, false, SignIn.Token, "gitee"),
    ];

    /// <summary>The provider with this id, or null for one this build does not know.</summary>
    public static DevProvider? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>The providers of one kind, in display order.</summary>
    public static IEnumerable<DevProvider> Of(DevKind kind) => All.Where(p => p.Kind == kind);
}
