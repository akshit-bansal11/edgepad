import Foundation
import Security

/// The Mac's `SecretStore`: one generic-password item per name in the user's Keychain, service
/// `me.akshitbansal.edgepad`, account = the name, readable only while the Mac is unlocked and never synced to another
/// device. Nothing here throws or logs the value: a failure reads as "no secret" (`get` is nil) or "not saved" (`set` is
/// false), and only the OSStatus is logged.
public final class KeychainSecretStore: SecretStore {
    public static let defaultService = "me.akshitbansal.edgepad"

    private static let maxName = 100

    private let service: String

    /// - Parameter service: The Keychain service the items live under; tests pass their own so they never touch the
    ///   app's.
    public init(service: String = KeychainSecretStore.defaultService) {
        self.service = service
    }

    public func get(_ name: String) -> String? {
        guard Self.valid(name) else { return nil }
        var query = base(name)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var found: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &found)
        guard status == errSecSuccess else {
            if status != errSecItemNotFound {
                Log.write("The Keychain could not be read: \(status)")
            }
            return nil
        }
        guard let data = found as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    public func set(_ name: String, _ value: String) -> Bool {
        guard Self.valid(name) else { return false }
        let data = Data(value.utf8)
        let update = SecItemUpdate(base(name) as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        if update == errSecSuccess { return true }
        guard update == errSecItemNotFound else {
            Log.write("The Keychain item could not be replaced: \(update)")
            return false
        }
        var item = base(name)
        item[kSecValueData as String] = data
        item[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        let status = SecItemAdd(item as CFDictionary, nil)
        guard status == errSecSuccess else {
            Log.write("The Keychain item could not be saved: \(status)")
            return false
        }
        return true
    }

    public func delete(_ name: String) {
        guard Self.valid(name) else { return }
        let status = SecItemDelete(base(name) as CFDictionary)
        if status != errSecSuccess && status != errSecItemNotFound {
            Log.write("The Keychain item could not be removed: \(status)")
        }
    }

    private func base(_ name: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: name,
        ]
    }

    private static func valid(_ name: String) -> Bool {
        (1...maxName).contains(name.utf8.count)
            && name.unicodeScalars.allSatisfy { $0.value > 0x20 && $0.value < 0x7F }
    }
}
