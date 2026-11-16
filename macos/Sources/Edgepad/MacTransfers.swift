import EdgepadCore
import Foundation

/// Where this Mac keeps a file the phone sends: `~/Downloads/Edgepad`, and the quarantine attribute that makes
/// Gatekeeper treat it as downloaded. The receiver (`TransferReceiver`) never overwrites and never opens what it saves.
enum MacTransfers {
    /// Where received files land.
    static var destination: String {
        let downloads =
            FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Downloads"
        return downloads + "/Edgepad"
    }

    /// Where a file still arriving, or cut short, is kept; the receiver deletes parts after a week.
    static var partialFolder: String {
        let support =
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Library/Application Support"
        return support + "/Edgepad/Partial"
    }

    /// Writes `com.apple.quarantine` on `path`, itself and not a link it might be. A volume that refuses it is logged
    /// and the file stays, since the bytes are saved and checked either way. It is called on the part, before the
    /// file is moved into Downloads, so the file is never there unmarked.
    static func markDownloaded(_ path: String) {
        let value = Array(Quarantine.value(at: Date()).utf8)
        if setxattr(path, Quarantine.attribute, value, value.count, 0, XATTR_NOFOLLOW) != 0 {
            Log.write("A received file could not be marked as downloaded (errno \(errno))")
        }
    }
}
