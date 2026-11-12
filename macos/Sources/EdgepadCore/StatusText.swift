import Foundation

/// The words the menu shows for a link's state, one place so they can be held against the canonical copy.
public enum StatusText {
    public static let starting = "Starting…"
    public static let waiting = "Waiting for your phone"

    /// `link` is "Bluetooth", "Wi-Fi" or "USB".
    public static func connected(_ name: String, over link: String) -> String {
        "Connected to \(name) · \(link)"
    }

    /// A refused handshake: the higher protocol number is the newer app, so the text names the older half. The
    /// numbers stay in the log.
    public static func mismatch(name: String, phoneVersion: UInt8, laptopVersion: UInt8) -> String {
        phoneVersion < laptopVersion
            ? "\(name) could not connect: its Edgepad app is older. Update Edgepad on the phone."
            : "\(name) could not connect: Edgepad on this laptop is older. Update Edgepad here."
    }

    /// Why Bluetooth did not start, from what macOS says about permission and power. `denied` wins: a user who
    /// switched Bluetooth on still gets nothing until Edgepad is allowed.
    public static func bluetoothProblem(denied: Bool, off: Bool) -> String {
        if denied {
            return "Edgepad is not allowed to use Bluetooth. Allow it in System Settings › Privacy & Security › "
                + "Bluetooth."
        }
        return off
            ? "Bluetooth is off. Turn it on in the menu bar." : "Bluetooth is off or unavailable; trying again"
    }
}

/// What a second launch does, decided on the versions alone.
public enum SingleInstance {
    public enum Decision: Equatable {
        /// The same version is already running: say so and exit.
        case exitWithNotice
        /// Only other versions (or ones whose version is unknown) are running: they are asked to quit.
        case takeOver
    }

    public static func decide(mine: String, others: [String?]) -> Decision {
        others.contains(mine) ? .exitWithNotice : .takeOver
    }
}
