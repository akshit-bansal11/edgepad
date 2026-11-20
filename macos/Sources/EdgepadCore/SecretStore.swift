import Foundation

/// The operating system's store for a secret such as the GitHub token: the Keychain on a Mac (`KeychainSecretStore` in
/// the app). Nothing here throws into its caller: a failure reads as "no secret" or "not saved". The twin of the laptop
/// core's ISecretStore.
public protocol SecretStore: AnyObject {
    /// The secret saved under `name`; nil when there is none or it cannot be read.
    func get(_ name: String) -> String?

    /// Saves `value` under `name`, replacing any earlier one; false when it could not be saved.
    func set(_ name: String, _ value: String) -> Bool

    /// Removes the secret under `name`; nothing happens when there is none.
    func delete(_ name: String)
}
