import AppKit
import EdgepadCore

/// Follows the apps macros open through `NSWorkspace`. No public, permission-free API minimizes another app's windows,
/// so on a Mac "minimize" hides the app (Cmd-H), "raise" unhides and activates it, and "close" quits it gracefully
/// (Cmd-Q), which may ask to save and is never a force-quit. Only an `.app` target with no arguments is tracked; a URL,
/// a folder, a document or a macro with arguments opens as it always did. Main thread only, like
/// `WorkspaceMacroLauncher`.
final class WorkspaceMacroWindows: MacroWindows {
    let available = true

    func survey(_ macros: [Macro]) -> [OpenState] {
        let running = WorkspaceMacroWindows.runningApps()
        return macros.map { macro in
            guard let bundle = MacroTarget.bundlePath(of: macro) else { return .untracked }
            guard let apps = running[bundle] else { return .closed }
            return apps.contains { $0.isActive } ? .front : .open
        }
    }

    func minimize(_ macro: Macro) -> Bool {
        instances(of: macro).map { $0.hide() }.contains(true)
    }

    func raise(_ macro: Macro) -> Bool {
        var raised = false
        for app in instances(of: macro) {
            app.unhide()
            raised = app.activate(options: [.activateAllWindows]) || raised
        }
        return raised
    }

    func close(_ macro: Macro) -> Bool {
        instances(of: macro).map { $0.terminate() }.contains(true)
    }

    private func instances(of macro: Macro) -> [NSRunningApplication] {
        guard let bundle = MacroTarget.bundlePath(of: macro) else { return [] }
        return WorkspaceMacroWindows.runningApps()[bundle] ?? []
    }

    /// Every running app by its canonical bundle path. Canonicalising touches the disk, so a survey does this once
    /// however many macros it covers.
    private static func runningApps() -> [String: [NSRunningApplication]] {
        var apps: [String: [NSRunningApplication]] = [:]
        for app in NSWorkspace.shared.runningApplications where !app.isTerminated {
            if let url = app.bundleURL {
                apps[MacroTarget.canonical(url), default: []].append(app)
            }
        }
        return apps
    }
}
