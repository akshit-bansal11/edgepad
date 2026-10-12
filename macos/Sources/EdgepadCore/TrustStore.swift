import Foundation

/// Trust on first use, with the owner's say-so. Pairing already limits connections to bonded devices; this
/// narrows it to one phone, so some other paired device cannot drive the Mac. The first phone to complete the
/// handshake while nothing is trusted is not taken on its word: the owner is asked, on the Mac, and only a yes
/// writes it down. Forget clears it, and the next phone is asked about in turn.
///
/// Reading is the guard that matters: a file that is there and cannot be read is not the same as no file, and
/// treating it as one would re-arm trust on first use and hand this Mac to whichever phone connected while
/// the file was unreadable. So a failed read refuses, and only a genuinely absent file admits.
public final class TrustStore {
    private let path: String
    private let gate = NSLock()

    /// The phone admitted when its address could not be written down. Kept for this run so a failed save
    /// narrows trust to that phone rather than leaving it open to whichever bonded device connects next.
    private var unsaved: String?

    /// Set while the owner is being asked about one phone. A second phone arriving then is refused unasked
    /// rather than stacking a second question on the first, which the owner could answer for the wrong phone.
    private var asking = false

    public init(path: String) {
        self.path = path
    }

    public static func forCurrentUser() -> TrustStore {
        let support =
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Library/Application Support"
        return TrustStore(path: support + "/Edgepad/trusted-phone.txt")
    }

    /// The trusted address, or nil when there is none or it cannot be read.
    public var trusted: String? {
        locked {
            let read = tryRead()
            return read.readable ? read.address ?? unsaved : nil
        }
    }

    /// True when `address` may connect. While nothing is trusted, `ask` puts the question to the owner and a
    /// yes is remembered; otherwise it is never called. It blocks for as long as the owner takes, so it runs
    /// outside the lock: the menu reads `trusted` on the very thread that is showing the question, and would
    /// otherwise wait on its own answer.
    public func admit(_ address: String, ask: () -> Bool) -> Bool {
        let early: Bool? = locked {
            if let known = decided(address) {
                return known
            }
            if asking {
                return false
            }
            asking = true
            return nil
        }
        if let early {
            return early
        }

        let yes = ask()
        locked { asking = false }
        if !yes {
            return false
        }

        return locked {
            // Settled again after the wait: a file edited by hand in that minute is still the one that counts.
            if let settled = decided(address) {
                return settled
            }
            do {
                try save(address)
            } catch {
                // Admitted anyway, and remembered until the app quits. Refusing the owner's own phone because
                // the disk is full would be a worse answer than forgetting it at the next start.
                unsaved = address
                Log.write("The trusted phone could not be saved to \(path): \(error.localizedDescription)")
            }
            return true
        }
    }

    /// True when `address` is certain to be refused: another phone is trusted, or the trust file cannot be
    /// read. Checked before a new connection may displace the current one; unlike `admit` it never grants trust.
    public func refuses(_ address: String) -> Bool {
        locked { decided(address) == false }
    }

    public func forget() {
        locked {
            unsaved = nil
            do {
                try FileManager.default.removeItem(atPath: path)
            } catch {
                if !isAbsent(error) {
                    Log.write("The trusted phone could not be forgotten at \(path): \(error.localizedDescription)")
                }
            }
        }
    }

    private func locked<T>(_ body: () -> T) -> T {
        gate.lock()
        defer { gate.unlock() }
        return body()
    }

    /// Whether `address` is admitted without asking: true for the trusted phone, false for any other and for a
    /// trust file that cannot be read, nil when nothing is trusted yet. Called under the lock.
    private func decided(_ address: String) -> Bool? {
        let read = tryRead()
        if !read.readable {
            // Fail closed. The phone sees a refusal, the log says why, and Forget is the way out; the
            // alternative is a Mac that quietly re-pairs itself whenever its own trust file is unreadable,
            // which is the single thing this class exists to prevent.
            return false
        }
        guard let trusted = read.address ?? unsaved else {
            return nil
        }
        return trusted.caseInsensitiveCompare(address) == .orderedSame
    }

    /// Readable is false when the file is there and could not be read: the caller must then refuse rather than
    /// guess. A nil address with readable true means nothing is trusted yet, which an absent file and a blank
    /// one both mean: Forget deletes the file, and a blank one is the same intent written by hand.
    private func tryRead() -> (readable: Bool, address: String?) {
        do {
            let text = try String(contentsOfFile: path, encoding: .utf8)
            let address = text.trimmingCharacters(in: .whitespacesAndNewlines)
            return (true, address.isEmpty ? nil : address)
        } catch {
            if isAbsent(error) {
                return (true, nil)
            }
            Log.write("The trusted phone could not be read from \(path): \(error.localizedDescription)")
            return (false, nil)
        }
    }

    /// Whether `error` says the file is genuinely not there, as opposed to there and unreadable. The thrown
    /// error decides first; stat settles what Foundation reports vaguely, such as a folder that is really a file.
    private func isAbsent(_ error: Error) -> Bool {
        let nsError = error as NSError
        if nsError.domain == NSCocoaErrorDomain && nsError.code == NSFileReadNoSuchFileError {
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

    /// The directory is private to the user and so is the file: the address says which device may drive this Mac.
    private func save(_ address: String) throws {
        let manager = FileManager.default
        let directory = (path as NSString).deletingLastPathComponent
        try manager.createDirectory(
            atPath: directory.isEmpty ? "." : directory,
            withIntermediateDirectories: true,
            attributes: [.posixPermissions: 0o700]
        )
        guard manager.createFile(atPath: path, contents: Data(address.utf8), attributes: [.posixPermissions: 0o600])
        else {
            throw CocoaError(.fileWriteUnknown)
        }
    }
}
