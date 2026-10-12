/// The handshake constants both apps and `protocol/actions.txt` agree on; a test holds them equal.
public enum ProtocolConstants {
    /// Moves only in a major release, and both halves must then be installed together.
    public static let version: UInt8 = 4
    /// "EDGP", the first thing in a HELLO, so a stray connection is not mistaken for a phone.
    public static let magic: [UInt8] = [0x45, 0x44, 0x47, 0x50]
    public static let serviceId = "758bb618-7b72-4cd3-9aa2-c9b88e54d555"
}
