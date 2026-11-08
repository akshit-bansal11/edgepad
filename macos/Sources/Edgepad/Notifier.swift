import Foundation
import UserNotifications

/// Desktop notifications for the two things that must be noticed when no window is in front: a phone asking to be
/// trusted or paired, and a new version. A click is handed back by the notification's identifier. Without the
/// owner's permission a post is quietly dropped; the menu and the alert still carry the same words.
final class Notifier: NSObject, UNUserNotificationCenterDelegate {
    private let center = UNUserNotificationCenter.current()
    private let onClick: (String) -> Void

    init(onClick: @escaping (String) -> Void) {
        self.onClick = onClick
        super.init()
        center.delegate = self
    }

    func requestPermission() {
        center.requestAuthorization(options: [.alert]) { _, _ in }
    }

    func post(id: String, title: String, body: String) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        center.add(UNNotificationRequest(identifier: id, content: content, trigger: nil))
    }

    func withdraw(id: String) {
        center.removeDeliveredNotifications(withIdentifiers: [id])
    }

    func userNotificationCenter(
        _ center: UNUserNotificationCenter, willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        completionHandler([.banner])
    }

    func userNotificationCenter(
        _ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
        withCompletionHandler completionHandler: @escaping () -> Void
    ) {
        let id = response.notification.request.identifier
        DispatchQueue.main.async { self.onClick(id) }
        completionHandler()
    }
}
