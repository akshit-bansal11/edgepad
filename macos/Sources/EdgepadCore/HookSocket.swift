import Foundation

/// Why the hook socket could not be opened. The text names the check, never a path's contents.
public struct HookSocketError: Error, Equatable {
    public let reason: String

    public init(_ reason: String) {
        self.reason = reason
    }
}

/// One connection of the hook channel, already known to come from this user.
public protocol HookStream: AnyObject {
    /// Up to `limit` bytes; empty at the end of the stream; nil when `deadline` passed, the read failed, or `close` was
    /// called (from any thread). Nil `deadline` waits until one of the others happens.
    func read(limit: Int, until deadline: Date?) -> [UInt8]?
    /// False when the far end left or the write took over 2 s.
    func write(_ bytes: [UInt8]) -> Bool
    /// Ends the connection both ways and wakes a read waiting on it. Idempotent, any thread.
    func close()
}

/// Where the hook channel listens for this user. The app's is `UnixHookListener`; tests pass their own.
public protocol HookListening: AnyObject {
    /// What hook.json names: the socket path.
    var endpoint: String { get }
    /// The next connection from this user, blocking; nil once `close` was called.
    func accept() -> HookStream?
    /// Stops accepting and removes the socket file. Idempotent, any thread.
    func close()
}

/// A connected Unix socket's descriptor as a `HookStream`. Writes never raise SIGPIPE and give up after 2 s; reads
/// wait in quarter-second slices so `close` from another thread ends them. The descriptor itself is closed when the
/// last reference goes, so no thread can ever read a number that was reused meanwhile.
public final class SocketStream: HookStream {
    private static let slice: Int32 = 250

    private let fd: Int32
    private let gate = NSLock()
    private var shut = false

    /// Takes ownership of `fd`.
    public init(fd: Int32) {
        self.fd = fd
        var on: Int32 = 1
        _ = setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &on, socklen_t(MemoryLayout<Int32>.size))
        var wait = timeval(tv_sec: 2, tv_usec: 0)
        _ = setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, &wait, socklen_t(MemoryLayout<timeval>.size))
    }

    deinit {
        Darwin.close(fd)
    }

    private var isShut: Bool {
        gate.lock()
        defer { gate.unlock() }
        return shut
    }

    public func read(limit: Int, until deadline: Date?) -> [UInt8]? {
        guard limit > 0 else { return nil }
        var buffer = [UInt8](repeating: 0, count: limit)
        while !isShut {
            var wait = Self.slice
            if let deadline {
                let left = deadline.timeIntervalSinceNow
                if left <= 0 { return nil }
                wait = Int32(min(Double(Self.slice), max(1, left * 1000)))
            }
            var entry = pollfd(fd: fd, events: Int16(POLLIN), revents: 0)
            let ready = poll(&entry, 1, wait)
            if ready < 0 && errno != EINTR { return nil }
            if ready <= 0 { continue }
            let count = buffer.withUnsafeMutableBytes { Darwin.read(fd, $0.baseAddress, limit) }
            if count >= 0 { return Array(buffer.prefix(count)) }
            if errno != EINTR && errno != EAGAIN { return nil }
        }
        return nil
    }

    public func write(_ bytes: [UInt8]) -> Bool {
        var sent = 0
        while sent < bytes.count {
            guard !isShut else { return false }
            let count = bytes.withUnsafeBytes { raw in
                Darwin.send(fd, raw.baseAddress?.advanced(by: sent), bytes.count - sent, 0)
            }
            if count > 0 {
                sent += count
            } else if count < 0 && errno == EINTR {
                continue
            } else {
                return false
            }
        }
        return true
    }

    public func close() {
        gate.lock()
        let first = !shut
        shut = true
        gate.unlock()
        if first {
            _ = shutdown(fd, SHUT_RDWR)
        }
    }
}

/// A Unix socket address for `path`, handed to `body`; nil when the path does not fit `sun_path`.
func withUnixAddress<T>(_ path: String, _ body: (UnsafePointer<sockaddr>, socklen_t) -> T) -> T? {
    var address = sockaddr_un()
    let bytes = Array(path.utf8)
    guard !bytes.isEmpty, bytes.count < MemoryLayout.size(ofValue: address.sun_path) else { return nil }
    address.sun_family = sa_family_t(AF_UNIX)
    address.sun_len = UInt8(MemoryLayout<sockaddr_un>.size)
    withUnsafeMutableBytes(of: &address.sun_path) { $0.copyBytes(from: bytes) }
    let size = socklen_t(MemoryLayout<sockaddr_un>.size)
    return withUnsafePointer(to: &address) { pointer in
        pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { body($0, size) }
    }
}

/// The uid on the other end of a connected Unix socket (`getpeereid`), or nil.
func peerUid(_ fd: Int32) -> uid_t? {
    var uid: uid_t = 0
    var gid: gid_t = 0
    return getpeereid(fd, &uid, &gid) == 0 ? uid : nil
}

/// The hook channel on a Mac: `hook.sock` in a folder only this user may enter (`$TMPDIR/edgepad`, `$TMPDIR` being
/// the per-user folder macOS gives every account). The folder is made 0700 when missing and refused when it is a link,
/// is not this user's or has any other mode. A stale socket is removed, the new one bound and made 0600, and the folder
/// checked again. Every connection's peer uid (`getpeereid`) must equal this process's effective uid, checked before a
/// byte is read; any other peer is closed at once. The twin of the Linux UnixSocketListener.
public final class UnixHookListener: HookListening {
    public static let socketName = "hook.sock"

    public let endpoint: String
    private let fd: Int32
    private let peer: (Int32) -> uid_t?
    private let myUid: uid_t
    private let gate = NSLock()
    private var closed = false

    /// Binds `hook.sock` in `directory`. Throws `HookSocketError` when the folder or the socket fails its checks.
    /// - Parameters:
    ///   - peer: For tests: the uid of a connected peer. `getpeereid` by default.
    ///   - myUid: For tests: this process's uid. `geteuid()` by default.
    public init(directory: String, peer: ((Int32) -> uid_t?)? = nil, myUid: uid_t = geteuid()) throws {
        self.peer = peer ?? peerUid
        self.myUid = myUid
        try Self.checkFolder(directory, owner: myUid)
        let path = (directory as NSString).appendingPathComponent(Self.socketName)
        endpoint = path
        fd = try Self.open(path, in: directory, owner: myUid)
    }

    deinit {
        Darwin.close(fd)
    }

    private var isClosed: Bool {
        gate.lock()
        defer { gate.unlock() }
        return closed
    }

    public func accept() -> HookStream? {
        while !isClosed {
            var entry = pollfd(fd: fd, events: Int16(POLLIN), revents: 0)
            let ready = poll(&entry, 1, 250)
            if ready <= 0 {
                if ready < 0 && errno != EINTR { usleep(100_000) }
                continue
            }
            let client = Darwin.accept(fd, nil, nil)
            if client < 0 {
                if errno != EINTR && errno != EAGAIN && errno != ECONNABORTED { usleep(100_000) }
                continue
            }
            if let uid = peer(client), uid == myUid {
                return SocketStream(fd: client)
            }
            Log.write("A hook connection from another user was closed")
            Darwin.close(client)
        }
        return nil
    }

    public func close() {
        gate.lock()
        let first = !closed
        closed = true
        gate.unlock()
        if first {
            unlink(endpoint)
        }
    }

    /// Creates the folder 0700 when missing; throws for a link, another owner or any other mode.
    static func checkFolder(_ directory: String, owner: uid_t) throws {
        var info = stat()
        if lstat(directory, &info) != 0 {
            guard errno == ENOENT, mkdir(directory, 0o700) == 0, chmod(directory, 0o700) == 0,
                lstat(directory, &info) == 0
            else { throw HookSocketError("The hook folder could not be made") }
        }
        guard info.st_mode & 0o170000 == 0o040000 else { throw HookSocketError("The hook folder is not a folder") }
        guard info.st_uid == owner else { throw HookSocketError("The hook folder is not this user's") }
        guard info.st_mode & 0o7777 == 0o700 else {
            throw HookSocketError("The hook folder is not private to this user")
        }
    }

    // A stale socket removed, then bound, made 0600, the folder checked again, listening. Closed again on any failure.
    private static func open(_ path: String, in directory: String, owner: uid_t) throws -> Int32 {
        unlink(path)
        let fd = socket(AF_UNIX, SOCK_STREAM, 0)
        guard fd >= 0 else { throw HookSocketError("The hook socket could not be made") }
        do {
            let bound = withUnixAddress(path) { Darwin.bind(fd, $0, $1) }
            guard bound == 0 else { throw HookSocketError("The hook socket could not be bound") }
            guard chmod(path, 0o600) == 0 else { throw HookSocketError("The hook socket could not be made private") }
            try checkFolder(directory, owner: owner)
            guard listen(fd, 8) == 0 else { throw HookSocketError("The hook socket could not listen") }
        } catch {
            Darwin.close(fd)
            unlink(path)
            throw error
        }
        return fd
    }
}
