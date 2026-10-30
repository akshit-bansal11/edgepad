import Foundation

/// Who this Mac is on the network link: 16 random bytes made once per install, and the name it shows. The id is what a
/// phone files its pairing key under, so it must outlive restarts; it is no secret, Bonjour announces it.
public struct LinkIdentity {
    public let id: [UInt8]
    public let name: String

    public var idHex: String { Hex.string(id) }

    /// The payload of TEXT DEVICE: "id/os/name", the id being the one the link's INFO reply gives.
    public var deviceText: String { "\(idHex)/\(DeviceOs.macos.rawValue)/\(name)" }

    public static func forCurrentUser(name: String) -> LinkIdentity {
        let support =
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Library/Application Support"
        return load(path: support + "/Edgepad/link-id.txt", name: name)
    }

    /// The id kept at `path`, or a new one written there when there is none or it is not one this app wrote. A file
    /// that is there and cannot be read is left alone and a new id is used for this run only: overwriting it would cost
    /// every paired phone its pairing for a read that may work next time.
    public static func load(path: String, name: String) -> LinkIdentity {
        do {
            let text = try String(contentsOfFile: path, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines)
            if let id = Hex.bytes(text), id.count == LinkWire.idLength {
                return LinkIdentity(id: id, name: name)
            }
        } catch {
            if !isAbsent(error, at: path) {
                Log.write(
                    "This Mac's link id could not be read from \(path), so a new one is used until it can: "
                        + error.localizedDescription)
                return LinkIdentity(id: Hex.random(LinkWire.idLength), name: name)
            }
        }
        let identity = LinkIdentity(id: Hex.random(LinkWire.idLength), name: name)
        if let failure = writePrivately(identity.idHex + "\n", to: path) {
            Log.write(
                "This Mac's link id could not be saved to \(path), so phones paired now must pair again after a "
                    + "restart: \(failure)")
        }
        return identity
    }
}

/// Lowercase hex, as the link's addresses and files write ids, and random bytes for ids and nonces.
enum Hex {
    static func string(_ bytes: [UInt8]) -> String {
        bytes.map { String(format: "%02x", $0) }.joined()
    }

    static func isDigit(_ byte: UInt8) -> Bool {
        (48...57).contains(byte) || (97...102).contains(byte | 0x20)
    }

    /// Nil unless `text` is an even number of hex digits.
    static func bytes(_ text: String) -> [UInt8]? {
        let digits = Array(text.utf8)
        guard digits.count % 2 == 0, digits.allSatisfy(isDigit) else { return nil }
        return stride(from: 0, to: digits.count, by: 2).map { value(digits[$0]) << 4 | value(digits[$0 + 1]) }
    }

    /// From the system's secure generator, which `UInt8.random` uses.
    static func random(_ count: Int) -> [UInt8] {
        (0..<count).map { _ in UInt8.random(in: 0...255) }
    }

    private static func value(_ digit: UInt8) -> UInt8 {
        digit <= 57 ? digit - 48 : (digit | 0x20) - 87
    }
}
