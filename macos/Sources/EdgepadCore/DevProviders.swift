import Foundation

/// Which developer page a provider belongs to.
public enum DevKind: String {
    case ai
    case git
}

/// How the laptop gets access to a provider: a device-code sign-in, a pasted read-only token, or a hook plugin.
public enum SignIn: String {
    case device
    case token
    case hook
}

/// One AI tool or git host. `id` is the `AgentToken` raw value of an AI tool and the settings key of both kinds,
/// `featureToken` the `Feature` raw value named while it is connected, `logoId` the logo's resource id.
public struct DevProvider: Equatable {
    public let id: String
    public let kind: DevKind
    public let name: String
    public let featureToken: String
    public let hasLimits: Bool
    public let flow: SignIn
    public let logoId: String
}

/// The one list of developer providers, in display order. No screen hard-codes a tool list. The twin of the laptop
/// core's DevProviders; both mirror the PROVIDER rows of protocol/actions.txt.
public enum DevProviders {
    public static let all: [DevProvider] = [
        DevProvider(
            id: "claude", kind: .ai, name: "Claude Code", featureToken: Feature.claude.rawValue, hasLimits: true,
            flow: .hook, logoId: "claude"),
        DevProvider(
            id: "codex", kind: .ai, name: "Codex", featureToken: Feature.codex.rawValue, hasLimits: true,
            flow: .hook, logoId: "openai"),
        DevProvider(
            id: "gemini", kind: .ai, name: "Gemini CLI", featureToken: Feature.gemini.rawValue, hasLimits: false,
            flow: .hook, logoId: "gemini"),
        DevProvider(
            id: "opencode", kind: .ai, name: "OpenCode", featureToken: Feature.opencode.rawValue,
            hasLimits: false, flow: .hook, logoId: "opencode"),
        DevProvider(
            id: "github", kind: .git, name: "GitHub", featureToken: Feature.github.rawValue, hasLimits: false,
            flow: .device, logoId: "github"),
        DevProvider(
            id: "gitlab", kind: .git, name: "GitLab", featureToken: Feature.gitlab.rawValue, hasLimits: false,
            flow: .device, logoId: "gitlab"),
        DevProvider(
            id: "gitea", kind: .git, name: "Gitea", featureToken: Feature.gitea.rawValue, hasLimits: false,
            flow: .token, logoId: "gitea"),
        DevProvider(
            id: "bitbucket", kind: .git, name: "Bitbucket", featureToken: Feature.bitbucket.rawValue,
            hasLimits: false, flow: .token, logoId: "bitbucket"),
        DevProvider(
            id: "gitee", kind: .git, name: "Gitee", featureToken: Feature.gitee.rawValue, hasLimits: false,
            flow: .token, logoId: "gitee"),
    ]

    /// The provider with this id, or nil for one this build does not know.
    public static func find(_ id: String) -> DevProvider? {
        all.first { $0.id == id }
    }

    /// The providers of one kind, in display order.
    public static func of(_ kind: DevKind) -> [DevProvider] {
        all.filter { $0.kind == kind }
    }
}
