import Foundation

/// The public half of the key that signs every release's SHA256SUMS.txt: a base64 SubjectPublicKeyInfo of a NIST
/// P-256 key. Empty until the owner's signing step fills it in; while empty no update is installed, the release
/// page is opened instead. A public key is not a secret, but it is never logged either.
public enum UpdateKey {
    public static let publicKeySpkiBase64 = ""

    public static var isSet: Bool {
        !publicKeySpkiBase64.isEmpty
    }

    /// The key's bytes; empty when it is unset or is not base64, which makes every verification fail.
    public static func spki() -> Data {
        Data(base64Encoded: publicKeySpkiBase64) ?? Data()
    }
}
