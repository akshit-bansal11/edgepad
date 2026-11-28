import AppKit
import EdgepadCore

/// Says which app is in front through `NSWorkspace`, with no polling: one notification observer exists only while a phone
/// watches. The bundle id is the token and the localised name the name; neither is ever logged. Main thread only.
final class WorkspaceForeground: ForegroundSource {
    /// The window the newest change waits for, so a burst of activations is one report.
    static let debounce: TimeInterval = 0.2

    let available = true

    func watch(_ onChange: @escaping (String) -> Void) -> ForegroundWatch {
        var pending: DispatchWorkItem?
        var last: String?
        var live = true

        let report = {
            guard live else { return }
            let app = NSWorkspace.shared.frontmostApplication
            let text = ForegroundApp.encode(token: app?.bundleIdentifier ?? "", name: app?.localizedName)
            guard text != last else { return }
            last = text
            onChange(text)
        }
        let observer = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main
        ) { _ in
            pending?.cancel()
            let work = DispatchWorkItem(block: report)
            pending = work
            DispatchQueue.main.asyncAfter(deadline: .now() + WorkspaceForeground.debounce, execute: work)
        }
        // The current app at once, as the phone asks to hear it from now.
        let first = DispatchWorkItem(block: report)
        pending = first
        DispatchQueue.main.async(execute: first)

        return ForegroundWatch {
            live = false
            pending?.cancel()
            NSWorkspace.shared.notificationCenter.removeObserver(observer)
        }
    }
}

extension WorkspaceForeground {
    /// The regular apps running now, the one in front first, then the `.app` bundles the macros point at, each as a TEXT 21
    /// payload with the bundle id as the token, as `watch` reports it. Asked once per phone request; nothing runs between.
    func openApps(macroTargets: [String]) -> [String] {
        let front = NSWorkspace.shared.frontmostApplication
        let running = NSWorkspace.shared.runningApplications
            .filter { $0.activationPolicy == .regular }
            .sorted { $0 == front && $1 != front }
            .map { ForegroundApp.encode(token: $0.bundleIdentifier ?? "", name: $0.localizedName) }
        let installed = macroTargets.filter { $0.hasSuffix(".app") }.map { path in
            ForegroundApp.encode(
                token: Bundle(path: path)?.bundleIdentifier ?? "",
                name: ((path as NSString).lastPathComponent as NSString).deletingPathExtension)
        }
        return running + installed
    }
}
