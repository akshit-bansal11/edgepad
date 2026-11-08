import AppKit

/// Tells VoiceOver what changed in a window the user cannot see change (WCAG 4.1.3).
enum Announce {
    static func say(_ text: String) {
        NSAccessibility.post(
            element: NSApp as Any, notification: .announcementRequested,
            userInfo: [
                .announcement: text,
                .priority: NSAccessibilityPriorityLevel.high.rawValue,
            ])
    }
}
