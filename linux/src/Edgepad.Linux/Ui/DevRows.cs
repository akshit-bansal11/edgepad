using System.Globalization;
using Edgepad.Dashboard;

namespace Edgepad.Ui;

/// <summary>Where an AI tool's connection stands, as the For developers window shows it.</summary>
internal enum AgentStatus
{
    NotConnected,
    Connected,

    /// <summary>The agent's command line is not on this computer.</summary>
    NotFound,

    /// <summary>Codex is older than 0.159.</summary>
    UpdateCodex,

    /// <summary>Codex has the plugin but the owner has not trusted its hooks in /hooks yet.</summary>
    AwaitingTrust,
}

/// <summary>
/// One AI tool row's state. <paramref name="Restart"/> asks the owner to restart open sessions; <paramref name="Commands"/> is set when
/// Edgepad could not run the vendor's commands itself, and holds them, one per line.
/// </summary>
internal sealed record AgentState(AgentStatus Status, DateTimeOffset? LastHeard = null, bool Restart = false, string? Commands = null);

/// <summary>What one row shows: its status line, the one button it offers (Go or Undo) and whether it has setup steps.</summary>
internal sealed record RowView(string Status, bool Dot, bool Calm, string? Go, bool GoEnabled, string? Undo, bool UndoEnabled, bool CanHowTo);

/// <summary>The For developers window's row rules, with no control in them so they can be tested.</summary>
internal static class DevRows
{
    /// <summary>The setup steps a provider's "How to connect" opens; empty for a provider with none (GitLab until its sign-in ships).</summary>
    internal static string StepsFor(string id) => id switch
    {
        "claude" => LinuxStrings.Integrations_ClaudeSteps,
        "codex" => LinuxStrings.Integrations_CodexSteps,
        "gemini" => LinuxStrings.Dev_GeminiSteps,
        "opencode" => LinuxStrings.Dev_OpenCodeSteps,
        "github" => LinuxStrings.Integrations_GitHubSteps,
        "gitea" => LinuxStrings.Dev_GiteaSteps,
        "bitbucket" => LinuxStrings.Dev_BitbucketSteps,
        "gitee" => LinuxStrings.Dev_GiteeSteps,
        _ => string.Empty,
    };

    /// <summary>The sentence for a failed token save; null for <see cref="GitSaveStatus.Saved"/>.</summary>
    internal static string? SaveMessage(GitSaveStatus status) => status switch
    {
        GitSaveStatus.Saved => null,
        GitSaveStatus.BadToken or GitSaveStatus.BadUrl or GitSaveStatus.BadUser => LinuxStrings.Dev_SaveInvalid,
        GitSaveStatus.Unauthorized => LinuxStrings.Dev_SaveUnauthorized,
        GitSaveStatus.Forbidden => LinuxStrings.Dev_SaveForbidden,
        GitSaveStatus.NotFound => LinuxStrings.Dev_SaveNotFound,
        GitSaveStatus.Network => LinuxStrings.Dev_SaveNetwork,
        _ => LinuxStrings.Dev_SaveFailed,
    };

    /// <summary>"14:05" today, "8 Nov" before, "never" when nothing was heard.</summary>
    internal static string Heard(DateTimeOffset? last, DateTimeOffset now, CultureInfo culture)
    {
        if (last is not { } at)
        {
            return LinuxStrings.Integrations_Never;
        }

        var local = at.ToLocalTime();
        return local.Date == now.ToLocalTime().Date ? local.ToString("t", culture) : local.ToString("d MMM", culture);
    }

    internal static bool IsConnected(AgentState? state) => state is { Status: AgentStatus.Connected or AgentStatus.AwaitingTrust };

    internal static string AiStatus(string name, AgentState state, DateTimeOffset now) => state.Status switch
    {
        AgentStatus.Connected => Say.Format(LinuxStrings.Integrations_Connected, Heard(state.LastHeard, now, CultureInfo.CurrentCulture)),
        AgentStatus.NotFound => Say.Format(LinuxStrings.Integrations_NotFound, name),
        AgentStatus.UpdateCodex => LinuxStrings.Integrations_UpdateCodex,
        AgentStatus.AwaitingTrust => LinuxStrings.Integrations_WaitingTrust,
        _ => LinuxStrings.Integrations_NotConnected,
    };

    /// <summary>An AI tool row. <paramref name="state"/> is null until the first read has come back.</summary>
    internal static RowView Ai(string name, AgentState? state, bool busy, DateTimeOffset now)
    {
        var connected = IsConnected(state);
        var ready = state is not null && !busy;
        return new RowView(
            ready ? AiStatus(name, state!, now) : LinuxStrings.Integrations_Working,
            Dot: ready && state!.Status == AgentStatus.Connected,
            Calm: !ready || state!.Status is AgentStatus.Connected or AgentStatus.NotConnected or AgentStatus.AwaitingTrust,
            Go: connected ? null : LinuxStrings.Integrations_Connect,
            GoEnabled: ready,
            Undo: connected ? LinuxStrings.Integrations_Disconnect : null,
            UndoEnabled: ready,
            CanHowTo: ready && !connected);
    }

    internal static RowView GitHub(string name, bool configured, bool signedIn, bool signingIn, bool codeShown, string login) => new(
        !configured ? LinuxStrings.Integrations_NoClientId
            : signedIn ? Say.Format(LinuxStrings.Integrations_SignedIn, login.Length > 0 ? login : name)
            : signingIn && !codeShown ? LinuxStrings.Integrations_Working
            : LinuxStrings.Integrations_NotConnected,
        Dot: signedIn,
        Calm: !signedIn,
        Go: signedIn || signingIn ? null : LinuxStrings.Dev_SignIn,
        GoEnabled: configured,
        Undo: signedIn ? LinuxStrings.Integrations_SignOut : signingIn ? LinuxStrings.Common_Cancel : null,
        UndoEnabled: true,
        CanHowTo: configured && !signedIn && !signingIn);

    /// <summary>GitLab's device sign-in needs a registered client ID; until then the row says so and does nothing.</summary>
    internal static RowView GitLab() =>
        new(LinuxStrings.Integrations_NotConnected, Dot: false, Calm: true, Go: LinuxStrings.Dev_ComingSoon, GoEnabled: false, Undo: null, UndoEnabled: false, CanHowTo: false);

    /// <summary>A token host (Gitea, Bitbucket, Gitee).</summary>
    internal static RowView Token(string name, bool signedIn, string login, bool saving) => new(
        signedIn ? Say.Format(LinuxStrings.Integrations_SignedIn, login.Length > 0 ? login : name) : LinuxStrings.Integrations_NotConnected,
        Dot: signedIn,
        Calm: !signedIn,
        Go: signedIn ? null : LinuxStrings.Dev_Add,
        GoEnabled: !saving,
        Undo: signedIn ? LinuxStrings.Integrations_SignOut : null,
        UndoEnabled: true,
        CanHowTo: !signedIn);
}
