import AppKit
import EdgepadCore

/// Follows the apps macros open through `NSWorkspace`. No public, permission-free API minimizes another app's windows,
/// so on a Mac "minimize" hides the app (Cmd-H), "raise" unhides and activates it, and "close" quits it gracefully
/// (Cmd-Q), which may ask to save and is never a force-quit. Only an `.app` target is tracked; a URL, a folder or a
/// document opens as it always did. Main thread only, like `WorkspaceMacroLauncher`.
final class WorkspaceMacroWindows: MacroWindows {
    let available = true

    func survey(_ macros: [Macro]) -> [OpenState] {
        let running = NSWorkspace.shared.runningApplications
        return macros.map { macro in
            guard let bundle = WorkspaceMacroWindows.bundlePath(macro.target) else { return .untracked }
            let apps = WorkspaceMacroWindows.instances(of: bundle, in: running)
            if apps.isEmpty {
                return .closed
            }
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
        guard let bundle = WorkspaceMacroWindows.bundlePath(macro.target) else { return [] }
        return WorkspaceMacroWindows.instances(of: bundle, in: NSWorkspace.shared.runningApplications)
    }

    private static func instances(of bundle: String, in running: [NSRunningApplication]) -> [NSRunningApplication] {
        running.filter { !$0.isTerminated && $0.bundleURL.map(canonical) == bundle }
    }

    /// The target's canonical path when it is an application bundle on this Mac, otherwise nil. A URL never gets
    /// through: nothing exists at a path spelled like one.
    private static func bundlePath(_ target: String) -> String? {
        let path = (target as NSString).expandingTildeInPath
        var isFolder: ObjCBool = false
        guard FileManager.default.fileExists(atPath: path, isDirectory: &isFolder), isFolder.boolValue else {
            return nil
        }
        let url = URL(fileURLWithPath: path)
        return url.pathExtension.lowercased() == "app" ? canonical(url) : nil
    }

    /// Paths rather than URLs, so a trailing slash cannot make one bundle two.
    private static func canonical(_ url: URL) -> String {
        url.standardizedFileURL.resolvingSymlinksInPath().path
    }
}
