import CryptoKit
import Foundation

/// The arithmetic of EDGL1, all of it CryptoKit: P-256 ECDH, HKDF and HMAC over SHA-256, AES-256-GCM. The twin of the
/// C# `LinkCrypto`, checked against the same vectors (protocol/link-vectors.txt), so a Mac and a phone derive the same
/// code and keys from the same exchange.
///
/// Every comparison of a tag, a commitment or a code goes through `fixedTimeEquals`: a compare that stops at the first
/// wrong byte tells a patient attacker how many leading bytes were right.
public enum LinkCrypto {
    static let keyLength = 32
    static let aeadTagLength = 16
    static let nonceLength = 12
    public static let codeDigits = 6

    private static let transcriptLabel = Array("EDGL1 th".utf8)
    private static let sasLabel = Array("EDGL1 sas".utf8)
    private static let commitLabel = Array("EDGL1 commit".utf8)
    private static let pairLabel = Array("pair".utf8)

    /// A fresh key pair, used for one connection.
    static func newKeyPair() -> P256.KeyAgreement.PrivateKey {
        P256.KeyAgreement.PrivateKey()
    }

    /// The 65-byte uncompressed point the wire carries.
    static func publicKey(_ key: P256.KeyAgreement.PrivateKey) -> [UInt8] {
        Array(key.publicKey.x963Representation)
    }

    /// The raw shared secret, the X coordinate of the shared point, as the other platforms give it. The peer's key
    /// must be a 65-byte uncompressed point on the curve: CryptoKit checks the curve on import, and anything it
    /// refuses is `LinkCryptoError.badKey`, after which the caller closes. The encoding cannot express infinity.
    static func agree(_ own: P256.KeyAgreement.PrivateKey, _ peerPublic: [UInt8]) throws -> [UInt8] {
        guard peerPublic.count == LinkWire.publicKeyLength, peerPublic[0] == 0x04 else {
            throw LinkCryptoError.badKey
        }
        do {
            let peer = try P256.KeyAgreement.PublicKey(x963Representation: peerPublic)
            let secret = try own.sharedSecretFromKeyAgreement(with: peer)
            return secret.withUnsafeBytes { Array($0) }
        } catch {
            throw LinkCryptoError.badKey
        }
    }

    /// HKDF-Extract (RFC 5869) with SHA-256.
    static func extract(salt: [UInt8], ikm: [UInt8]) -> [UInt8] {
        Array(HKDF<SHA256>.extract(inputKeyMaterial: SymmetricKey(data: ikm), salt: salt))
    }

    /// HKDF-Expand (RFC 5869) with SHA-256.
    static func expand(_ prk: [UInt8], _ info: String, _ length: Int) -> [UInt8] {
        HKDF<SHA256>.expand(pseudoRandomKey: prk, info: Array(info.utf8), outputByteCount: length)
            .withUnsafeBytes { Array($0) }
    }

    static func hash(_ parts: [[UInt8]]) -> [UInt8] {
        var hash = SHA256()
        for part in parts {
            hash.update(data: part)
        }
        return Array(hash.finalize())
    }

    static func hmac(_ key: [UInt8], _ parts: [[UInt8]]) -> [UInt8] {
        var mac = HMAC<SHA256>(key: SymmetricKey(data: key))
        for part in parts {
            mac.update(data: part)
        }
        return Array(mac.finalize())
    }

    /// What the phone binds itself to before it sees the Mac's key and nonce.
    static func commit(_ phonePublic: [UInt8], _ phoneNonce: [UInt8]) -> [UInt8] {
        hash([commitLabel, phonePublic, phoneNonce])
    }

    /// Everything both sides said in a pairing, in one hash, names as sent (`LinkWire.nameField`): any difference
    /// between their views changes the code and every key.
    static func pairTranscript(
        phoneId: [UInt8], laptopId: [UInt8], phoneName: [UInt8], laptopName: [UInt8], phonePublic: [UInt8],
        phoneNonce: [UInt8], laptopPublic: [UInt8], laptopNonce: [UInt8]
    ) -> [UInt8] {
        hash([
            transcriptLabel, [LinkWire.modePair], phoneId, laptopId, phoneName, laptopName, phonePublic, phoneNonce,
            laptopPublic, laptopNonce,
        ])
    }

    /// A resume's transcript: no names travel in a resume, so none are in it.
    static func resumeTranscript(
        phoneId: [UInt8], laptopId: [UInt8], phonePublic: [UInt8], phoneNonce: [UInt8], laptopPublic: [UInt8],
        laptopNonce: [UInt8]
    ) -> [UInt8] {
        hash([
            transcriptLabel, [LinkWire.modeResume], phoneId, laptopId, phonePublic, phoneNonce, laptopPublic,
            laptopNonce,
        ])
    }

    /// The six digits the phone shows and the Mac's owner types: the first four bytes of a hash of the transcript,
    /// big-endian, mod a million, zero padded. Anyone on the connection can work it out; what it proves is only that
    /// the two ends saw the same transcript, which a relay in the middle cannot arrange.
    static func sas(_ transcript: [UInt8]) -> String {
        let digest = hash([sasLabel, transcript])
        let value = digest[0..<4].reduce(UInt32(0)) { $0 << 8 | UInt32($1) } % 1_000_000
        let digits = String(value)
        return String(repeating: "0", count: codeDigits - digits.count) + digits
    }

    /// Whether the owner typed exactly the six digits of `sas`, compared in constant time.
    static func codeMatches(_ typed: String, _ sas: String) -> Bool {
        let bytes = Array(typed.utf8)
        return bytes.count == codeDigits && bytes.allSatisfy { (48...57).contains($0) }
            && fixedTimeEquals(bytes, Array(sas.utf8))
    }

    /// Equal lengths and equal bytes, looking at every byte whatever the first difference.
    static func fixedTimeEquals(_ a: [UInt8], _ b: [UInt8]) -> Bool {
        guard a.count == b.count else { return false }
        var difference: UInt8 = 0
        for index in a.indices {
            difference |= a[index] ^ b[index]
        }
        return difference == 0
    }

    static func pairPrk(_ transcript: [UInt8], _ shared: [UInt8]) -> [UInt8] {
        extract(salt: transcript, ikm: shared)
    }

    /// A resume mixes the pairing key into a fresh exchange: forward secrecy from the one, the pairing from the other.
    static func resumePrk(_ transcript: [UInt8], _ shared: [UInt8], _ pairingKey: [UInt8]) -> [UInt8] {
        extract(salt: transcript, ikm: shared + pairingKey)
    }

    /// The Mac's key confirmation. In a pairing it is sent only after the owner typed the phone's code.
    static func laptopTag(_ prk: [UInt8], _ transcript: [UInt8], pairing: Bool) -> [UInt8] {
        confirmTag(prk, "EDGL1 confirm laptop", transcript, pairing)
    }

    static func phoneTag(_ prk: [UInt8], _ transcript: [UInt8], pairing: Bool) -> [UInt8] {
        confirmTag(prk, "EDGL1 confirm phone", transcript, pairing)
    }

    /// The key both ends keep after a pairing, under the other's id, to resume with.
    static func pairingKey(_ prk: [UInt8]) -> [UInt8] {
        expand(prk, "EDGL1 pairing key", keyLength)
    }

    /// The record keys for each direction, and the 8 bytes whose halves prefix each direction's nonces.
    static func sessionKeys(_ prk: [UInt8]) -> (phoneToLaptop: [UInt8], laptopToPhone: [UInt8], iv: [UInt8]) {
        (expand(prk, "EDGL1 key p2l", keyLength), expand(prk, "EDGL1 key l2p", keyLength), expand(prk, "EDGL1 iv", 8))
    }

    /// A record nonce: the direction's 4-byte prefix, then the record counter, big-endian.
    static func nonce(_ prefix: [UInt8], _ counter: UInt64) -> [UInt8] {
        prefix + (0..<8).map { UInt8(truncatingIfNeeded: counter >> (56 - 8 * UInt64($0))) }
    }

    /// The ciphertext followed by its 16-byte tag.
    static func seal(_ key: [UInt8], _ nonce: [UInt8], _ plain: [UInt8], _ aad: [UInt8]) throws -> [UInt8] {
        let box = try AES.GCM.seal(
            plain, using: SymmetricKey(data: key), nonce: AES.GCM.Nonce(data: nonce), authenticating: aad)
        return Array(box.ciphertext) + Array(box.tag)
    }

    /// Opens what `seal` made; a record that fails its tag throws.
    static func open(_ key: [UInt8], _ nonce: [UInt8], _ sealed: [UInt8], _ aad: [UInt8]) throws -> [UInt8] {
        let box = try AES.GCM.SealedBox(
            nonce: AES.GCM.Nonce(data: nonce), ciphertext: Array(sealed.dropLast(aeadTagLength)),
            tag: Array(sealed.suffix(aeadTagLength)))
        let plain = try AES.GCM.open(box, using: SymmetricKey(data: key), authenticating: aad)
        return Array(plain)
    }

    private static func confirmTag(_ prk: [UInt8], _ label: String, _ th: [UInt8], _ pairing: Bool) -> [UInt8] {
        let key = expand(prk, label, keyLength)
        return pairing ? hmac(key, [pairLabel, th]) : hmac(key, [th])
    }
}

enum LinkCryptoError: Error {
    /// The peer's key is not an uncompressed P-256 point on the curve.
    case badKey
}
