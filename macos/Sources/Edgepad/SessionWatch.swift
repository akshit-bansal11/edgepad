import AppKit
import EdgepadCore

/// Holds this Mac off while the user's session is not in front: another user switched in, or the screen locked. The two
/// workspace notifications are public; `com.apple.screenIsLocked` and `com.apple.screenIsUnlocked` are not documented by
/// Apple but are widely used, and they need confirming on a current macOS. Four observers, no timer. Main thread only.
final class SessionWatch {
    private let hold: () -> Void
    private let resume: () -> Void
    private var state = SessionHold()
    private var tokens: [(center: NotificationCenter, token: NSObjectProtocol)] = []

    /// `hold` runs on the first reason and `resume` when the last one clears, each exactly once per hold.
    init(hold: @escaping () -> Void, resume: @escaping () -> Void) {
        self.hold = hold
        self.resume = resume
    }

    func start() {
        guard tokens.isEmpty else { return }
        let workspace = NSWorkspace.shared.notificationCenter
        let distributed = DistributedNotificationCenter.default()
        observe(workspace, NSWorkspace.sessionDidResignActiveNotification, begin: .switchedOut)
        observe(workspace, NSWorkspace.sessionDidBecomeActiveNotification, end: .switchedOut)
        observe(distributed, Notification.Name("com.apple.screenIsLocked"), begin: .locked)
        observe(distributed, Notification.Name("com.apple.screenIsUnlocked"), end: .locked)
    }

    /// Removes the observers and resumes any hold this started.
    func stop() {
        for entry in tokens {
            entry.center.removeObserver(entry.token)
        }
        tokens.removeAll()
        if state.clear() {
            resume()
        }
    }

    private func observe(_ center: NotificationCenter, _ name: Notification.Name, begin: SessionHold.Reason) {
        add(center, name) { [weak self] in
            if self?.state.begin(begin) == true {
                self?.hold()
            }
        }
    }

    private func observe(_ center: NotificationCenter, _ name: Notification.Name, end: SessionHold.Reason) {
        add(center, name) { [weak self] in
            if self?.state.end(end) == true {
                self?.resume()
            }
        }
    }

    private func add(_ center: NotificationCenter, _ name: Notification.Name, _ body: @escaping () -> Void) {
        let token = center.addObserver(forName: name, object: nil, queue: .main) { _ in body() }
        tokens.append((center: center, token: token))
    }
}
