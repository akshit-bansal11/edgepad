import CryptoKit

/// Puts one blob back together from what the phone sent, and trusts none of it. Nothing is allocated from the size a
/// BLOB_BEGIN claims: the buffer grows with the bytes that arrived and stops at that size, which `cap` has already
/// bounded, so at most one capped blob is held at a time.
///
/// A blob that goes wrong is drained to its BLOB_END and dropped, and the next BLOB_BEGIN starts clean: the framing was
/// valid, so the link stays up. A refused blob answers false once, for the dispatcher's count, and is quiet after that.
/// The Swift mirror of the shared core's `BlobReceiver`.
final class BlobReceiver {
    /// All the blob bytes one connection may send, kept or drained. Past it every blob is refused.
    static let maxConnectionBytes = 256 * 1024 * 1024

    private let cap: (UInt8) -> Int?
    private let completed: (UInt8, [UInt8], [UInt8]) -> Bool

    private var buffer: [UInt8]?
    private var draining = false
    private var kind: UInt8 = 0
    private var size = 0
    private var seq: UInt8 = 0
    private var spent = 0

    /// - Parameters:
    ///   - cap: The most bytes a kind may carry, or nil for a kind this side does not take.
    ///   - completed: Handed the kind, the bytes and their checked SHA-256; false drops and counts them.
    init(cap: @escaping (UInt8) -> Int?, completed: @escaping (UInt8, [UInt8], [UInt8]) -> Bool) {
        self.cap = cap
        self.completed = completed
    }

    /// Starts a blob, aborting any still open: the sender gave up on that one.
    func take(kind: UInt8, size: UInt32, label: String) -> Bool {
        reset()
        let claimed = Int(size)
        guard let limit = cap(kind), claimed <= limit, spent + claimed <= BlobReceiver.maxConnectionBytes,
            !label.unicodeScalars.contains(where: { $0.properties.generalCategory == .control })
        else {
            draining = true
            return false
        }
        self.kind = kind
        self.size = claimed
        seq = 0
        buffer = []
        return true
    }

    func take(seq chunk: UInt8, data: [UInt8]) -> Bool {
        spent += data.count
        if draining {
            return true
        }
        guard let held = buffer?.count else { return false }
        // An empty chunk is refused so the number of chunks is bounded by the size, not only their bytes.
        guard !data.isEmpty, chunk == seq, held + data.count <= size else {
            buffer = nil
            draining = true
            return false
        }
        buffer?.append(contentsOf: data)
        seq &+= 1
        return true
    }

    func take(sha256: [UInt8]) -> Bool {
        let open = buffer
        let drained = draining
        reset()
        if drained {
            return true
        }
        guard let open, open.count == size else { return false }
        return Array(SHA256.hash(data: open)) == sha256 && completed(kind, open, sha256)
    }

    /// Forgets any blob in progress. What this connection has already spent stays spent.
    func reset() {
        buffer = nil
        draining = false
    }
}
