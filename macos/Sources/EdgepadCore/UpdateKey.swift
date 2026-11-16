import Foundation

/// The public half of the key that signs every release's SHA256SUMS.txt: a base64 SubjectPublicKeyInfo of a NIST
/// P-256 key, the same line as protocol/update-key.txt. Were it empty, no update would be installed and the release
/// page opened instead. A public key is not a secret, but it is never logged either.
public enum UpdateKey {
    // Split only to fit the line length; the test holds it equal to protocol/update-key.txt.
    public static let publicKeySpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEKIO+dzmbn/k8aLGN5vAKTvguNumWGY8btuwc"
        + "pUEMCCMa+GPz9jPPHs3QBi7dOPVZRCyCOrTmtibnj2Ex5K3dIA=="

    public static var isSet: Bool {
        !publicKeySpkiBase64.isEmpty
    }

    /// The key's bytes; empty when it is unset or is not base64, which makes every verification fail.
    public static func spki() -> Data {
        Data(base64Encoded: publicKeySpkiBase64) ?? Data()
    }
}
