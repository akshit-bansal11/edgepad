import Foundation

/// The front tab of a scriptable browser, asked with Apple Events. macOS asks the owner once per browser; a denial or
/// any other failure is an osascript error and reads as nil, so the phone shows its plain symbol. Firefox has no
/// scripting for tabs, so it is not here.
enum BrowserTab {
    /// Bundle id to the dictionary's word for the front window's tab: Chromium browsers say `active tab`, Safari
    /// `current tab` and `name` for the title.
    private static let browsers: [String: (tab: String, title: String)] = [
        "com.google.Chrome": ("active tab", "title"),
        "com.microsoft.edgemac": ("active tab", "title"),
        "com.brave.Browser": ("active tab", "title"),
        "company.thebrowser.Browser": ("active tab", "title"),
        "com.apple.Safari": ("current tab", "name"),
    ]

    static func isScriptable(_ bundleId: String) -> Bool {
        browsers[bundleId] != nil
    }

    /// The tab's title and host, one script off the main thread's caller. Nil when the browser has no window or macOS
    /// said no.
    static func read(_ bundleId: String) -> (title: String, host: String)? {
        guard let words = browsers[bundleId] else { return nil }
        let output = NowPlayingHub.osascript([
            "tell application id \"\(bundleId)\"", "set t to \(words.tab) of front window",
            "return (\(words.title) of t) & linefeed & (URL of t)", "end tell",
        ])
        guard let lines = output?.split(separator: "\n", maxSplits: 1, omittingEmptySubsequences: false),
            lines.count == 2
        else { return nil }
        let host = URL(string: lines[1].trimmingCharacters(in: .whitespacesAndNewlines))?.host ?? ""
        return (String(lines[0]), host)
    }
}
