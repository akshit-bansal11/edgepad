import Foundation

/// The cleartext half of the network link, EDGL1, the twin of the C# `LinkWire`: the bytes a phone and a Mac exchange
/// before anything is encrypted, and only their shape. Every message is `u16 length | body`, little-endian, at most
/// `maxMessage` bytes of body, so a peer that never authenticates can never make this side hold more than that. What
/// the values mean, and in what order they are sent, is `LinkResponder`'s.
///
/// Requests (phone to Mac) open with "EDGL", the link version and a mode. Replies (Mac to phone) open with a status
/// byte; any status but `statusOk` is the whole reply, and the Mac closes after it.
enum LinkWire {
    static let linkVersion: UInt8 = 1

    static let modeInfo: UInt8 = 0
    static let modePair: UInt8 = 1
    static let modeResume: UInt8 = 2

    static let statusOk: UInt8 = 0
    /// Another pairing is under way, or pairing is locked after failed attempts.
    static let statusBusy: UInt8 = 1
    /// The phone's link version is not this Mac's.
    static let statusUnsupported: UInt8 = 2
    /// A resume from a phone this Mac holds no pairing key for: the phone should pair again.
    static let statusUnknownPhone: UInt8 = 3

    static let maxMessage = 512
    static let maxRecordPlain = 16384

    static let idLength = 16
    static let nonceLength = 16
    static let publicKeyLength = 65
    static let hashLength = 32
    static let maxNameBytes = 255

    static let magic: [UInt8] = Array("EDGL".utf8)

    /// One message as it goes on the wire: the body's length, then the body.
    static func message(_ body: [UInt8]) -> [UInt8] {
        precondition(body.count <= maxMessage, "A link message is at most \(maxMessage) bytes")
        return [UInt8(body.count & 0xFF), UInt8(body.count >> 8)] + body
    }

    /// A display name as the wire carries it: a one-byte length, then UTF-8 cut at a character boundary to fit. Both
    /// names go into the pairing transcript in exactly this form, as sent, so a relay that changes either one changes
    /// the code.
    static func nameField(_ name: String) -> [UInt8] {
        let utf8 = Array(name.utf8)
        var length = min(utf8.count, maxNameBytes)
        while length < utf8.count && length > 0 && utf8[length] & 0xC0 == 0x80 {
            length -= 1
        }
        return [UInt8(length)] + Array(utf8[0..<length])
    }

    static func status(_ status: UInt8) -> [UInt8] {
        [status]
    }

    static func infoReply(laptopId: [UInt8], protocolVersion: UInt8, name: String) -> [UInt8] {
        [statusOk] + laptopId + [protocolVersion] + nameField(name)
    }

    static func pairOffer(laptopId: [UInt8], name: String, publicKey: [UInt8], nonce: [UInt8]) -> [UInt8] {
        [statusOk] + laptopId + nameField(name) + publicKey + nonce
    }

    static func resumeReply(publicKey: [UInt8], nonce: [UInt8], tag: [UInt8]) -> [UInt8] {
        [statusOk] + publicKey + nonce + tag
    }

    /// A key-confirmation tag on its own, as both sides send one.
    static func tag(_ tag: [UInt8]) -> [UInt8] {
        tag
    }

    static func infoRequest() -> [UInt8] {
        magic + [linkVersion, modeInfo]
    }

    static func pairRequest(phoneId: [UInt8], name: String, commit: [UInt8]) -> [UInt8] {
        magic + [linkVersion, modePair] + phoneId + nameField(name) + commit
    }

    static func resumeRequest(phoneId: [UInt8], publicKey: [UInt8], nonce: [UInt8]) -> [UInt8] {
        magic + [linkVersion, modeResume] + phoneId + publicKey + nonce
    }

    /// The phone's key and nonce, sent only once the Mac's are on the wire: they open its commitment.
    static func reveal(publicKey: [UInt8], nonce: [UInt8]) -> [UInt8] {
        publicKey + nonce
    }

    /// A phone's opening message. A link version this Mac does not speak decodes as `.unsupported` without reading
    /// further, since its layout is not this one's to know.
    static func decodeRequest(_ body: [UInt8]) throws -> LinkRequest {
        var reader = LinkFieldReader(body)
        guard try reader.bytes(magic.count) == magic else {
            throw LinkWireError("Not an Edgepad link request")
        }
        let version = try reader.byte()
        guard version == linkVersion else {
            return .unsupported(version: version)
        }
        let request: LinkRequest
        switch try reader.byte() {
        case modeInfo:
            request = .info
        case modePair:
            let phoneId = try reader.bytes(idLength)
            let field = try reader.nameField()
            let commit = try reader.bytes(hashLength)
            request = .pair(phoneId: phoneId, name: text(field), nameField: field, commit: commit)
        case modeResume:
            let phoneId = try reader.bytes(idLength)
            let publicKey = try reader.bytes(publicKeyLength)
            let nonce = try reader.bytes(nonceLength)
            request = .resume(phoneId: phoneId, publicKey: publicKey, nonce: nonce)
        case let mode:
            throw LinkWireError("Unknown link mode \(mode)")
        }
        try reader.end()
        return request
    }

    static func decodeReveal(_ body: [UInt8]) throws -> (publicKey: [UInt8], nonce: [UInt8]) {
        var reader = LinkFieldReader(body)
        let publicKey = try reader.bytes(publicKeyLength)
        let nonce = try reader.bytes(nonceLength)
        try reader.end()
        return (publicKey, nonce)
    }

    static func decodeTag(_ body: [UInt8]) throws -> [UInt8] {
        var reader = LinkFieldReader(body)
        let tag = try reader.bytes(hashLength)
        try reader.end()
        return tag
    }

    /// The text of a name field; bytes that are not UTF-8 read as replacement characters, as on Windows.
    static func text(_ nameField: [UInt8]) -> String {
        String(decoding: nameField.dropFirst(), as: UTF8.self)
    }
}

/// A phone's opening message. `nameField` is the name as sent, length byte included, for the transcript.
enum LinkRequest: Equatable {
    case info
    case unsupported(version: UInt8)
    case pair(phoneId: [UInt8], name: String, nameField: [UInt8], commit: [UInt8])
    case resume(phoneId: [UInt8], publicKey: [UInt8], nonce: [UInt8])
}

/// A cleartext message that is not one EDGL1 sends. Its text names the problem, never a value from the message.
struct LinkWireError: Error, CustomStringConvertible {
    let description: String

    init(_ description: String) {
        self.description = description
    }
}

/// Reads one message's fields in order; every read past the end, and anything left after the last, throws.
struct LinkFieldReader {
    private let body: [UInt8]
    private var position = 0

    init(_ body: [UInt8]) {
        self.body = body
    }

    mutating func bytes(_ length: Int) throws -> [UInt8] {
        guard body.count - position >= length else {
            throw LinkWireError("A link message ended early")
        }
        defer { position += length }
        return Array(body[position..<position + length])
    }

    mutating func byte() throws -> UInt8 {
        try bytes(1)[0]
    }

    /// A name with its length byte, exactly as sent.
    mutating func nameField() throws -> [UInt8] {
        let length = try byte()
        let name = try bytes(Int(length))
        return [length] + name
    }

    func end() throws {
        guard position == body.count else {
            throw LinkWireError("A link message ran on past its fields")
        }
    }
}

/// Cuts the bytes a connection delivers into messages, one at a time, and hands back whatever came after the last one
/// taken: the phone may send its first encrypted record in the same packet as its last cleartext message.
struct LinkMessageReader {
    private var buffer: [UInt8] = []

    /// Bytes waiting that are not yet a whole message, or come after the last one taken.
    var remainder: [UInt8] { buffer }

    mutating func append(_ bytes: [UInt8]) {
        buffer += bytes
    }

    /// The next whole message's body, or nil until one has arrived. A length over the limit throws.
    mutating func next() throws -> [UInt8]? {
        guard buffer.count >= 2 else { return nil }
        let length = Int(buffer[0]) | Int(buffer[1]) << 8
        guard length <= LinkWire.maxMessage else {
            throw LinkWireError("A link message of \(length) bytes, over the \(LinkWire.maxMessage) allowed")
        }
        guard buffer.count >= 2 + length else { return nil }
        let body = Array(buffer[2..<2 + length])
        buffer.removeFirst(2 + length)
        return body
    }
}
