import Foundation

/// The encrypted half of the network link, the twin of the C# `SecureStream`: AES-256-GCM records over the raw
/// connection, so that a `Session` above it reads and writes frames exactly as it does over RFCOMM. `send` seals what
/// the session sends; `open` takes what the connection delivers and gives back the plaintext for `Session.receive`.
///
/// A record is `u16 length | ciphertext | 16-byte tag`, the length counting the tag and authenticated as the record's
/// associated data. The nonce is never sent: it is the direction's 4-byte prefix and a counter both ends keep, so a
/// record replayed, dropped or moved fails its tag like a forged one. Any record that fails, a length out of range,
/// or a write the connection refuses, fails the link for good: nothing more is opened or sent.
///
/// Single-threaded, like `Session`: the app calls it on the main thread.
public final class SecureTransport: Transport {
    private let inner: Transport
    private let sendKey: [UInt8]
    private let receiveKey: [UInt8]
    private let sendPrefix: [UInt8]
    private let receivePrefix: [UInt8]

    private var sent: UInt64 = 0
    private var received: UInt64 = 0
    /// Bytes of a record not yet whole: at most one record's worth plus what the last read brought.
    private var inbox: [UInt8] = []

    /// Set by the first failure. A link that has carried one forged or misplaced record, or lost part of one on the
    /// way out, is not trusted with another in either direction.
    public private(set) var failed = false

    /// `prk` is the handshake's secret, from which both directions' keys are taken; `isPhone` says which end this is,
    /// since each sends with the other's receive key. Only the tests are ever the phone.
    public init(inner: Transport, prk: [UInt8], isPhone: Bool = false) {
        self.inner = inner
        let keys = LinkCrypto.sessionKeys(prk)
        sendKey = isPhone ? keys.phoneToLaptop : keys.laptopToPhone
        receiveKey = isPhone ? keys.laptopToPhone : keys.phoneToLaptop
        sendPrefix = Array(isPhone ? keys.iv[0..<4] : keys.iv[4..<8])
        receivePrefix = Array(isPhone ? keys.iv[4..<8] : keys.iv[0..<4])
    }

    /// Seals `bytes` into as many records as it takes, each at most 16 KiB of plaintext, and sends them. False when
    /// the link has failed, now or before.
    public func send(_ bytes: [UInt8]) -> Bool {
        guard !failed else { return false }
        var records: [UInt8] = []
        var offset = 0
        while offset < bytes.count {
            let chunk = Array(bytes[offset..<min(offset + LinkWire.maxRecordPlain, bytes.count)])
            let length = chunk.count + LinkCrypto.aeadTagLength
            let header = [UInt8(length & 0xFF), UInt8(length >> 8)]
            let nonce = LinkCrypto.nonce(sendPrefix, sent)
            // Spent before anything can fail: a nonce is sealed under once, whatever then happens to the record.
            sent += 1
            guard let sealed = try? LinkCrypto.seal(sendKey, nonce, chunk, header) else {
                failed = true
                return false
            }
            records += header + sealed
            offset += chunk.count
        }
        // Part of a record may be on its way, and the phone would read whatever came next as its continuation.
        guard inner.send(records) else {
            failed = true
            return false
        }
        return true
    }

    /// Sealed bytes, tags and headers included, since those are what fill the connection's outbox.
    public var backlog: Int { inner.backlog }

    public func close() {
        failed = true
        inner.close()
    }

    /// The plaintext of every whole record in what has arrived so far, possibly none; the rest waits for the next
    /// read. Nil when a record fails or its length is out of range: the caller closes.
    public func open(_ bytes: [UInt8]) -> [UInt8]? {
        guard !failed else { return nil }
        inbox += bytes
        var plain: [UInt8] = []
        var offset = 0
        while inbox.count - offset >= 2 {
            let length = Int(inbox[offset]) | Int(inbox[offset + 1]) << 8
            // A record always carries at least one byte: `send` never seals an empty one.
            guard length > LinkCrypto.aeadTagLength, length <= LinkWire.maxRecordPlain + LinkCrypto.aeadTagLength
            else {
                failed = true
                return nil
            }
            guard inbox.count - offset - 2 >= length else { break }
            let header = Array(inbox[offset..<offset + 2])
            let sealed = Array(inbox[offset + 2..<offset + 2 + length])
            guard
                let opened = try? LinkCrypto.open(
                    receiveKey, LinkCrypto.nonce(receivePrefix, received), sealed, header)
            else {
                failed = true
                return nil
            }
            received += 1
            plain += opened
            offset += 2 + length
        }
        inbox.removeFirst(offset)
        return plain
    }
}
