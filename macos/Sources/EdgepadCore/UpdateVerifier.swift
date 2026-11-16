import CryptoKit
import Foundation

/// Decides whether a downloaded release file may be installed. The caller's order, each step stopping everything
/// after it on failure: fetch SHA256SUMS.txt, fetch its signature (`SHA256SUMS.txt.sig`), `verifySums` against
/// `UpdateKey`, `hashFor` the exact asset name of the offered version, download to a ".download" file,
/// `hashesMatch`, and only then move the file into place. Naming `Edgepad-{offered}-mac.zip` is what stops a
/// replayed, older signed release from being installed as a different version.
public enum UpdateVerifier {
    private static let hashHexLength = 64

    /// True only when `derSignature` is a valid ECDSA P-256 / SHA-256 signature of `sums` under `spki`.
    /// Anything else, including an unreadable key, is false.
    public static func verifySums(_ sums: Data, derSignature: Data, spki: Data) -> Bool {
        guard !sums.isEmpty, !derSignature.isEmpty, !spki.isEmpty else {
            return false
        }
        do {
            let key = try P256.Signing.PublicKey(derRepresentation: spki)
            // Trailing bytes after the key are refused: the key must be exactly what it re-encodes to.
            guard key.derRepresentation == spki else {
                return false
            }
            let signature = try P256.Signing.ECDSASignature(derRepresentation: derSignature)
            return key.isValidSignature(signature, for: sums)
        } catch {
            Log.write("The update signature could not be checked: \(type(of: error))")
            return false
        }
    }

    /// The SHA-256 the list `sums` gives `fileName`. Lines are sha256sum's: 64 lowercase hex digits, then
    /// "  name" or " *name". A line in any other shape, a name holding / or \, or the asked-for name listed twice
    /// makes the whole list unusable.
    public static func hashFor(sums: String, fileName: String) -> Data? {
        if fileName.isEmpty || fileName.contains("/") || fileName.contains("\\") {
            return nil
        }
        var found: Data?
        // Bytes, not Characters: Swift folds "\r\n" into one Character.
        for rawLine in Array(sums.utf8).split(separator: 10, omittingEmptySubsequences: false) {
            var line = Array(rawLine)
            if line.last == 13 {
                line.removeLast()
            }
            if line.isEmpty {
                continue
            }
            guard let (hash, name) = parseLine(line) else {
                return nil
            }
            if name == fileName {
                if found != nil {
                    return nil
                }
                found = hash
            }
        }
        return found
    }

    public static func hash(of data: Data) -> Data {
        Data(SHA256.hash(data: data))
    }

    /// The SHA-256 of the file at `url`, read in chunks; nil when it cannot be read.
    public static func hashFile(at url: URL) -> Data? {
        guard let handle = try? FileHandle(forReadingFrom: url) else {
            return nil
        }
        defer { try? handle.close() }
        var hasher = SHA256()
        do {
            // read(upToCount:) returns nil at the end of the file, not an empty Data.
            while let chunk = try handle.read(upToCount: 81920), !chunk.isEmpty {
                hasher.update(data: chunk)
            }
        } catch {
            return nil
        }
        return Data(hasher.finalize())
    }

    /// Equal 32-byte hashes, compared without stopping at the first difference.
    public static func hashesMatch(_ a: Data, _ b: Data) -> Bool {
        guard a.count == 32, b.count == 32 else {
            return false
        }
        var difference: UInt8 = 0
        for (x, y) in zip(a, b) {
            difference |= x ^ y
        }
        return difference == 0
    }

    private static func parseLine(_ line: [UInt8]) -> (Data, String)? {
        guard line.count > hashHexLength + 2, line[hashHexLength] == 32,
            line[hashHexLength + 1] == 32 || line[hashHexLength + 1] == 42
        else {
            return nil
        }
        var hash = Data()
        var high: UInt8?
        for c in line[0..<hashHexLength] {
            let digit: UInt8
            switch c {
            case 48...57: digit = c - 48
            case 97...102: digit = c - 87
            default: return nil
            }
            if let h = high {
                hash.append(h << 4 | digit)
                high = nil
            } else {
                high = digit
            }
        }
        let nameBytes = line[(hashHexLength + 2)...]
        guard !nameBytes.contains(47), !nameBytes.contains(92),
            let name = String(bytes: nameBytes, encoding: .utf8)
        else {
            return nil
        }
        return (hash, name)
    }
}
