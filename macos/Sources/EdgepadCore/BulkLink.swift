import Foundation

/// What the first frame of a connection that has just proved itself is. A transfer socket opens with BULK_HELLO where a
/// phone opens with HELLO, so the server looks before it makes a session: only a HELLO becomes one.
public enum FirstFrame: Equatable {
    /// Not enough bytes yet to tell.
    case needMore
    /// BULK_HELLO of `version`, and whatever followed it in the same read.
    case bulk(version: UInt8, rest: [UInt8])
    /// Anything else: the bytes go to a session, which refuses what is not a HELLO.
    case other
    /// Opens like BULK_HELLO and is not one.
    case invalid

    public static func classify(_ bytes: [UInt8]) -> FirstFrame {
        let sample = FrameCodec.encode(.bulkHello(version: 0))
        guard let first = bytes.first else { return .needMore }
        guard first == sample[0] else { return .other }
        guard bytes.count >= sample.count else { return .needMore }
        var reader = FrameReader()
        guard let frames = try? reader.append(Array(bytes[..<sample.count])),
            case .bulkHello(let version)? = frames.first
        else { return .invalid }
        return .bulk(version: version, rest: Array(bytes[sample.count...]))
    }
}

/// A phone's second socket, for file transfers only. It is never a session: it asks no trust question, takes no PIN,
/// never admits or replaces anything, and exists only as the `Session` it was attached to holds it, for exactly the
/// address that session has. The session ending, a frame that is not part of a file transfer (BULK_HELLO again
/// included), 30 s without a frame, or a write that fails closes it, and closing it leaves the session as it was.
public final class BulkLink {
    /// 30 s at the session's two polls a second, matching `Handshake.BulkSilenceLimit` in the shared core.
    public static let silentPollsLimit = 60

    private let address: String
    private let transport: Transport
    private let onFrame: (Frame) -> Void
    private var onClosed: (() -> Void)?
    private var reader = FrameReader()
    private var silentPolls = 0

    /// True once HELLO_ACK went out, so the session may send on it.
    public private(set) var isLive = false
    public private(set) var isClosed = false

    /// - Parameters:
    ///   - address: The phone's address, for the log.
    ///   - transport: The socket.
    ///   - onFrame: Handed each XFER frame that arrives.
    ///   - onClosed: Called once, when this is closed, whichever end closed it.
    public init(
        address: String, transport: Transport, onFrame: @escaping (Frame) -> Void, onClosed: @escaping () -> Void
    ) {
        self.address = address
        self.transport = transport
        self.onFrame = onFrame
        self.onClosed = onClosed
    }

    /// What the socket still holds.
    public var backlog: Int { transport.backlog }

    /// The only frames a transfer socket may carry once attached.
    public static func isLegal(_ frame: Frame) -> Bool {
        switch frame {
        case .xferBegin, .xferChunk, .xferEnd, .xferStatus:
            return true
        default:
            return false
        }
    }

    /// Answers HELLO_ACK. False, and closed, when the socket would not take it.
    public func start() -> Bool {
        guard send(.helloAck(version: ProtocolConstants.version)) else { return false }
        isLive = true
        return true
    }

    /// False, and closed, when the socket is dead or so far behind that it may as well be.
    public func send(_ frame: Frame) -> Bool {
        guard !isClosed, transport.send(FrameCodec.encode(frame)) else {
            close()
            return false
        }
        return true
    }

    /// Plain bytes from the phone, in arrival order.
    public func receive(_ bytes: [UInt8]) {
        guard !isClosed else { return }
        silentPolls = 0
        let frames: [Frame]
        do {
            frames = try reader.append(bytes)
        } catch {
            Log.write("Transfer socket of \(address) closed: \(error)")
            close()
            return
        }
        for frame in frames {
            guard !isClosed else { return }
            guard BulkLink.isLegal(frame) else {
                Log.write("Transfer socket of \(address) closed: it sent a frame that is not part of a file transfer")
                close()
                return
            }
            onFrame(frame)
        }
    }

    /// Called with the session's poll, twice a second: no timer of its own.
    public func tick() {
        guard !isClosed else { return }
        silentPolls += 1
        if silentPolls >= BulkLink.silentPollsLimit {
            Log.write("Transfer socket of \(address) closed: no frame for 30 s")
            close()
        }
    }

    public func close() {
        guard !isClosed else { return }
        isClosed = true
        isLive = false
        transport.close()
        let closed = onClosed
        onClosed = nil
        closed?()
    }
}
