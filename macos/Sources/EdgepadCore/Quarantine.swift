import Foundation

/// The `com.apple.quarantine` attribute a browser puts on a download, so Gatekeeper asks before anything received from
/// the phone is opened. Writing it is the app's; this builds the value.
public enum Quarantine {
    public static let attribute = "com.apple.quarantine"

    /// `0081;<seconds since 1970, hex>;Edgepad;`, the form a download carries: flags, time, the agent that saved it, and
    /// an event id that is left empty. The exact flags a Mac expects of a non-browser agent are UNVERIFIED on hardware.
    public static func value(at time: Date) -> String {
        let seconds = UInt64(max(0, time.timeIntervalSince1970))
        return "0081;\(String(seconds, radix: 16));Edgepad;"
    }
}
