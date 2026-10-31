import Foundation

/// Writes `text` to `path` through a temporary file beside it and a rename, so a crash or a full disk mid-write
/// leaves the old file whole rather than a half-written one. The folder is private to the user (0700) and so is the
/// file (0600): the PIN hash, the trusted phones and the macro list are all things no other account should read.
/// One copy for the stores that share the rule; `TrustStore` rewrites in place on purpose and does not use it.
///
/// Nil on success, otherwise why it failed, for the caller to put in its own log line.
func writePrivately(_ text: String, to path: String) -> String? {
    let manager = FileManager.default
    let temp = path + ".tmp"
    do {
        try manager.createDirectory(
            atPath: (path as NSString).deletingLastPathComponent,
            withIntermediateDirectories: true,
            attributes: [.posixPermissions: 0o700]
        )
    } catch {
        return error.localizedDescription
    }
    let data = Data(text.utf8)
    guard manager.createFile(atPath: temp, contents: data, attributes: [.posixPermissions: 0o600]) else {
        return "the file could not be written"
    }
    guard rename(temp, path) == 0 else {
        let reason = String(cString: strerror(errno))
        try? manager.removeItem(atPath: temp)
        return reason
    }
    return nil
}
