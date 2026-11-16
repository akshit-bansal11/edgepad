import Foundation

/// The wire status of an XFER_STATUS frame, in the order protocol 6 numbers them. The last two are this process's own
/// answers from `TransferSender`, never put on the wire. The Swift mirror of the shared core's `XferCode`.
public enum XferCode: UInt8 {
    /// Receiver to sender: go ahead, starting at the frame's offset.
    case ready = 0
    /// Receiver to sender: the contiguous bytes held so far.
    case progress = 1
    case saved = 2
    case badHash = 3
    case rejected = 4
    case writeFailed = 5
    case cancelled = 6
    case busy = 7
    /// Local only: no answer came in time.
    case timedOut = 8
    /// Local only: the session ended under the transfer.
    case sessionEnded = 9
}

/// The numbers both ends of a transfer hold to. D-XFER in the 5.0.0 plan.
public enum TransferLimits {
    /// The most one file may be: 2 GiB.
    public static let maxBytes: UInt64 = 2 * 1024 * 1024 * 1024
    /// Free space that must remain after the file, so a transfer cannot fill the disk.
    public static let freeSpaceMargin: Int64 = 64 * 1024 * 1024
    /// The receiver answers PROGRESS each time this many more bytes are held.
    public static let progressEveryBytes: UInt64 = 256 * 1024
    /// A partial file older than this is deleted when the receiver is created.
    public static let partialKept: TimeInterval = 7 * 24 * 60 * 60
    /// How long a sender waits for READY, and for the last answer after END.
    public static let answerTimeout: TimeInterval = 30
    /// A receiver drops a transfer that has had no chunk for this long, keeping the part for a resume.
    public static let idleTimeout: TimeInterval = 30
    /// The most of a file name kept, in UTF-8 bytes.
    public static let maxNameBytes = 120
    /// The longest extension kept when a name is cut, in UTF-8 bytes.
    public static let maxExtensionBytes = 20
    public static let partialExtension = ".part"
}
