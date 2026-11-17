import Foundation

/// The words the menu shows for a link's state, one place so they can be held against the canonical copy.
public enum StatusText {
    public static let starting = tr("Status_Starting", "Starting…")
    public static let waiting = tr("Status_Waiting", "Waiting for your phone")

    /// A file name cut in the middle with an ellipsis so the extension stays visible (DES-D29): at 16 characters,
    /// "quarterly-report-final.pdf" becomes "quarterly…al.pdf". A name that fits is returned as it is; one with no
    /// short extension is cut at the end.
    public static func middleTruncated(_ name: String, limit: Int = 36) -> String {
        guard name.count > limit else { return name }
        let ext = (name as NSString).pathExtension
        let tail = (1...8).contains(ext.count) ? ext.count + 3 : 0
        let head = limit - 1 - tail
        return tail == 0 ? "\(name.prefix(head))…" : "\(name.prefix(head))…\(name.suffix(tail))"
    }

    /// `link` is "Bluetooth", "Wi-Fi" or "USB".
    public static func connected(_ name: String, over link: String) -> String {
        tr("Core_ConnectedOver", "Connected to %1$@ · %2$@", name, link)
    }

    /// A refused handshake: the higher protocol number is the newer app, so the text names the older half. The
    /// numbers stay in the log.
    public static func mismatch(name: String, phoneVersion: UInt8, laptopVersion: UInt8) -> String {
        phoneVersion < laptopVersion
            ? tr(
                "Status_MismatchPhoneOlder",
                "%1$@ could not connect: its Edgepad app is older. Update Edgepad on the phone.", name)
            : tr(
                "Status_MismatchLaptopOlder",
                "%1$@ could not connect: Edgepad on this laptop is older. Update Edgepad here.", name)
    }

    /// Why Bluetooth did not start, from what macOS says about permission and power. `denied` wins: a user who
    /// switched Bluetooth on still gets nothing until Edgepad is allowed.
    public static func bluetoothProblem(denied: Bool, off: Bool) -> String {
        if denied {
            return tr(
                "Core_BluetoothNotAllowed",
                "Edgepad is not allowed to use Bluetooth. Allow it in System Settings › Privacy & Security › "
                    + "Bluetooth.")
        }
        return off
            ? tr("Core_BluetoothOffMenu", "Bluetooth is off. Turn it on in the menu bar.")
            : tr("Status_BluetoothRetrying", "Bluetooth is off or unavailable; trying again")
    }

    /// The status line while the owner types the pairing code for `phone`.
    public static func pairing(_ phone: String) -> String {
        tr("Status_Pairing", "Pair %1$@: type the pairing code shown on the phone", phone)
    }

    public static func paired(_ phone: String) -> String {
        tr("Status_Paired", "Paired with %1$@", phone)
    }

    /// The status line while the trust question about `phone` is open.
    public static func asking(_ phone: String) -> String {
        tr("Status_Asking", "Asking whether to trust %1$@", phone)
    }

    /// Where a Wi-Fi request came from, "from 192.168.1.23, on your network"; `address` may be empty.
    public static func networkSource(_ address: String) -> String {
        let network = tr("Source_OnNetwork", "on your network")
        return address.isEmpty ? network : tr("Source_From", "from %1$@, %2$@", address, network)
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
