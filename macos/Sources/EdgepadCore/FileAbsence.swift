import Foundation

/// Whether `error` says the file at `path` is genuinely not there, as opposed to there and unreadable. The thrown
/// error decides first; stat settles what Foundation reports vaguely, such as a folder that is really a file. One
/// test for both stores, because "absent" decides whether a read fails open (no PIN, nobody trusted) or closed.
func isAbsent(_ error: Error, at path: String) -> Bool {
    let nsError = error as NSError
    let missing = [NSFileReadNoSuchFileError, NSFileNoSuchFileError]
    if nsError.domain == NSCocoaErrorDomain && missing.contains(nsError.code) {
        return true
    }
    if let underlying = nsError.userInfo[NSUnderlyingErrorKey] as? NSError,
        underlying.domain == NSPOSIXErrorDomain,
        underlying.code == Int(ENOENT) || underlying.code == Int(ENOTDIR)
    {
        return true
    }
    var info = stat()
    return stat(path, &info) != 0 && (errno == ENOENT || errno == ENOTDIR)
}
