import Foundation

/// What a developer row's one button does.
public enum DevAction: Equatable {
    case connect
    case disconnect
    case signIn
    case signOut
    case cancel
    case add
}

/// What a row's status line says. The window turns each into its own translated words.
public enum DevLine: Equatable {
    /// A Connect or Disconnect is running.
    case working
    /// An AI tool's state, from the installer or the config writer.
    case agent(PluginState)
    /// Signed in; `name` is the account, or the provider's own name when none is known.
    case signedIn(String)
    /// A sign-in was asked for and its code is not shown yet.
    case waiting
    /// This build has no GitHub client id.
    case notConfigured
    case notConnected
}

/// What one row knows right now. The window fills it from the settings, the installers and its own sign-in state.
public struct DevRowInput {
    public var developerOn = false
    /// The provider's settings switch.
    public var connected = false
    public var busy = false
    public var agent = PluginState.notConnected
    /// GitHub: a client id is built in, signed in, a sign-in is running, its code is on screen.
    public var configured = true
    public var signedIn = false
    public var signingIn = false
    public var codeShown = false
    public var account = ""
    /// A token host's form is saving.
    public var saving = false

    public init() {}
}

/// One row as the window draws it: one status line with an optional green dot, at most one button, and whether "How to
/// connect" is offered. With developer mode off the button is disabled; the window disables the link too.
public struct DevRowView: Equatable {
    public let line: DevLine
    public let dot: Bool
    public let calm: Bool
    public let button: DevAction?
    public let buttonEnabled: Bool
    public let canHowTo: Bool
}

/// Why a token could not be saved. The window gives each its own sentence.
public enum DevSaveNote: Equatable {
    case invalid
    case unauthorized
    case forbidden
    case notFound
    case network
    case failed
}

/// The rules of the "For developers" window that need no window: which line and button a row shows, and what a failed
/// token save says. The twin of the Windows window's `View` and `SaveMessage`.
public enum DevRowModel {
    public static func view(_ provider: DevProvider, _ input: DevRowInput) -> DevRowView {
        let view = make(provider, input)
        return DevRowView(
            line: view.line, dot: view.dot, calm: view.calm, button: view.button,
            buttonEnabled: view.buttonEnabled && input.developerOn, canHowTo: view.canHowTo)
    }

    /// A provider has setup steps unless it is GitLab, whose two ways in (a device code, a pasted server token) need
    /// none.
    public static func hasSteps(_ id: String) -> Bool {
        id != "gitlab"
    }

    /// The failed-save reason, or nil for a saved token.
    public static func note(_ status: GitSaveStatus) -> DevSaveNote? {
        switch status {
        case .saved: return nil
        case .badToken, .badUrl, .badUser: return .invalid
        case .unauthorized: return .unauthorized
        case .forbidden: return .forbidden
        case .notFound: return .notFound
        case .network: return .network
        case .failed: return .failed
        }
    }

    private static func make(_ provider: DevProvider, _ input: DevRowInput) -> DevRowView {
        switch provider.kind {
        case .ai:
            let connected = input.connected
            return DevRowView(
                line: input.busy ? .working : .agent(input.agent),
                dot: !input.busy && input.agent == .connected,
                calm: input.busy || input.agent == .connected || input.agent == .notConnected,
                button: connected ? .disconnect : .connect, buttonEnabled: !input.busy,
                canHowTo: !connected && !input.busy)
        case .git:
            switch provider.id {
            case "github": return github(input)
            case "gitlab": return gitlab(input)
            default:
                return DevRowView(
                    line: input.signedIn ? .signedIn(input.account) : .notConnected, dot: input.signedIn,
                    calm: !input.signedIn, button: input.signedIn ? .signOut : .add,
                    buttonEnabled: input.signedIn || !input.saving, canHowTo: !input.signedIn)
            }
        }
    }

    private static func gitlab(_ input: DevRowInput) -> DevRowView {
        let line: DevLine
        if input.signedIn {
            line = .signedIn(input.account)
        } else if input.signingIn && !input.codeShown {
            line = .waiting
        } else {
            line = .notConnected
        }
        let button: DevAction = input.signedIn ? .signOut : input.signingIn ? .cancel : .signIn
        return DevRowView(
            line: line, dot: input.signedIn, calm: !input.signedIn, button: button,
            buttonEnabled: button == .signIn ? !input.saving : true, canHowTo: false)
    }

    private static func github(_ input: DevRowInput) -> DevRowView {
        let line: DevLine
        if !input.configured {
            line = .notConfigured
        } else if input.signedIn {
            line = .signedIn(input.account)
        } else if input.signingIn && !input.codeShown {
            line = .waiting
        } else {
            line = .notConnected
        }
        let button: DevAction = input.signedIn ? .signOut : input.signingIn ? .cancel : .signIn
        return DevRowView(
            line: line, dot: input.signedIn, calm: !input.signedIn, button: button,
            buttonEnabled: button == .signIn ? input.configured : true,
            canHowTo: input.configured && !input.signedIn && !input.signingIn)
    }
}

/// The git hosts besides GitHub that the window signs in: GitLab (gitlab.com by device flow, or a self-hosted server
/// by a pasted token), Gitea (Forgejo, Codeberg), Bitbucket and Gitee. Saving a token the host accepts, or finishing
/// the device flow, turns that host's switch on and keeps its login for the window; signing out forgets the token and
/// turns the switch off. GitHub signs in by device flow and is not here.
public final class DevTokenHosts {
    public static let ids = ["gitlab", "gitea", "bitbucket", "gitee"]

    private let hosts: [String: TokenGitHost]
    private let settings: IntegrationSettings

    public init(hosts: [TokenGitHost], settings: IntegrationSettings) {
        var byId: [String: TokenGitHost] = [:]
        for host in hosts {
            byId[host.id] = host
        }
        self.hosts = byId
        self.settings = settings
    }

    /// The hosts, in the registry's order, for `DashboardParts`.
    public var all: [GitHost] {
        Self.ids.compactMap { hosts[$0] }
    }

    /// Whether a token is saved for the host with this provider id.
    public func isSignedIn(_ id: String) -> Bool {
        hosts[id]?.isSignedIn ?? false
    }

    /// The login the last save named, for display only; empty when none.
    public func login(_ id: String) -> String {
        settings.text(Self.loginKey(id))
    }

    /// The address saved for the host (Gitea, Forgejo and Codeberg only); empty when none.
    public func url(_ id: String) -> String {
        settings.text(GitTokens.urlKey(id))
    }

    /// The user name or e-mail saved for the host (Bitbucket only); empty when none.
    public func user(_ id: String) -> String {
        settings.text(GitTokens.userKey(id))
    }

    public static func loginKey(_ id: String) -> String {
        id + "Login"
    }

    /// Checks and saves a pasted read-only token (see `TokenGitHost.save`); only when the host accepts it, turns the
    /// host's switch on and keeps the login. The caller then tells the dashboard.
    public func save(_ id: String, token: String, url: String, user: String) async -> GitSaveResult {
        guard let host = hosts[id] else { return GitSaveResult(status: .failed, login: "") }
        // GitLab's form is for a self-hosted server only: gitlab.com signs in by the device flow.
        let result: GitSaveResult
        if let gitlab = host as? GitLabHost {
            result = await gitlab.saveServer(token: token, url: url)
        } else {
            result = await host.save(token: token, url: url, user: user)
        }
        if result.status == .saved {
            settings.setFlag(id, true)
            settings.set(Self.loginKey(id), result.login)
        }
        return result
    }

    /// The device flow ended signed in: turns the host's switch on and keeps its login (asked of the host) for the
    /// window.
    public func finishDeviceSignIn(_ id: String) async {
        let name = await hosts[id]?.login()
        settings.setFlag(id, true)
        settings.set(Self.loginKey(id), name ?? "")
    }

    /// Forgets the host's token and turns its switch off.
    public func signOut(_ id: String) {
        hosts[id]?.signOut()
        settings.setFlag(id, false)
        settings.set(Self.loginKey(id), "")
    }
}
