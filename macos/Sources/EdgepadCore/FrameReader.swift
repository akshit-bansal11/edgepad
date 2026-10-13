/// Reassembles frames from a byte stream. Bluetooth hands over arbitrary chunks: half a frame, or three
/// frames and the start of a fourth.
public struct FrameReader {
    private var buffer: [UInt8] = []

    public init() {}

    /// Adds bytes and returns every frame now complete, in order; an incomplete tail is kept for the next call.
    /// Throws ProtocolError for an unknown type or a malformed frame; the caller then closes the connection.
    /// Linear in the bytes given: a read offset walks the buffer and it is compacted once per call, not once
    /// per frame.
    public mutating func append(_ bytes: [UInt8]) throws -> [Frame] {
        buffer.append(contentsOf: bytes)
        var frames: [Frame] = []
        var offset = 0

        parsing: while offset < buffer.count {
            let type = buffer[offset]
            let available = buffer.count - offset
            let total: Int
            switch FrameCodec.payloadLength(type) {
            case .unknown:
                throw ProtocolError.unknownType(type)
            case .fixed(let length):
                total = 1 + length
            case .lengthPrefixed:
                guard available >= 1 + FrameCodec.textHeaderLength else {
                    break parsing
                }
                total = 1 + FrameCodec.textHeaderLength + Int(buffer[offset + 2])
            }
            guard available >= total else {
                break parsing
            }
            let payload = Array(buffer[(offset + 1)..<(offset + total)])
            frames.append(try FrameCodec.decode(type: type, payload: payload))
            offset += total
        }

        buffer.removeFirst(offset)
        return frames
    }
}
