import Foundation

/// Trust with the owner's say-so, one phone at a time. Pairing already limits connections to bonded devices; this
/// narrows it to the phones the owner has accepted, so some other paired device cannot drive the Mac. A phone that
/// completes the handshake is not taken on its word: unless it is already trusted the owner is asked, on the Mac,
/// and only a yes writes its address down, one per line. A trusted phone is let in without asking. Forget clears
/// every one of them, and each phone after it is asked about again.
///
/// Reading is the guard that matters: a file that is there and cannot be read is not the same as no file, and
/// treating it as one would hand this Mac to whichever phone connected while the file was unreadable, and let the
/// next yes overwrite the phones it still names. So a failed read refuses every phone, and only a genuinely absent
/// file starts the list empty.
public final class TrustStore {
    private let path: String
    private let gate = NSLock()

    /// The phones admitted when their addresses could not be written down. Kept for this run so a disk that will
    /// not take an address costs the owner one question per run, not one at every reconnect.
    private var unsaved: [String] = []

    /// Set while the owner is being asked about one phone. A second phone arriving then is refused unasked
    /// rather than stacking a second question on the first, which the owner could answer for the wrong phone.
    private var asking = false

    /// Set when `forget()` could not delete the file. Until it really is gone every phone is refused, exactly as
    /// for a file that cannot be read: the phones are still trusted on disk, and the owner believes they are not.
    private var forgetFailed = false

    /// Moved on by every `forget()`. A Forget chosen while the owner is being asked is the later word: a yes that
    /// arrives after it saves nothing.
    private var generation = 0

    /// How long a phone the owner said no to is turned away unasked. Long enough that a phone's automatic
    /// retries cannot put the question straight back under the owner's cursor.
    public static let quietAfterDeclineSeconds: TimeInterval = 30

    /// The phone the owner last declined, and the time the quiet period ends. A paired device could otherwise
    /// raise the modal question again the instant it is answered, for ever.
    private var declined: (address: String, until: TimeInterval)?

    /// Uptime, not the wall clock, so a clock stepped backwards cannot stretch the quiet period.
    private let now: () -> TimeInterval

    public init(path: String, now: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime }) {
        self.path = path
        self.now = now
    }

    public static func forCurrentUser() -> TrustStore {
        let support =
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Library/Application Support"
        return TrustStore(path: support + "/Edgepad/trusted-phone.txt")
    }

    /// Where the trusted phones are kept, for a message that has to send the owner there.
    public var filePath: String { path }

    /// The trusted addresses, oldest first; empty when there are none or they cannot be read.
    public var trustedPhones: [String] {
        locked {
            let read = tryRead()
            return read.readable ? withUnsaved(read.addresses) : []
        }
    }

    /// True when there is something for Forget to clear: a trusted phone, or a trust file that cannot be read,
    /// which refuses every phone until it is removed.
    public var canForget: Bool {
        locked {
            let read = tryRead()
            return !read.readable || !withUnsaved(read.addresses).isEmpty
        }
    }

    /// True when `address` may connect. A phone not trusted yet is put to the owner through `ask`, and a yes adds
    /// it to the list; for a trusted one `ask` is never called. It blocks for as long as the owner takes, so it runs
    /// outside the lock: the menu reads `trustedPhones` on the very thread that is showing the question, and would
    /// otherwise wait on its own answer. An `ask` that throws (the session ended under the question) rethrows here
    /// with nothing recorded: the owner never answered, so the phone is not taken as declined.
    public func admit(_ address: String, ask: () throws -> Bool) rethrows -> Bool {
        var askedIn = 0
        let early: Bool? = locked {
            if let known = decided(address) {
                return known
            }
            if isDeclined(address) {
                return false
            }
            if asking {
                return false
            }
            asking = true
            askedIn = generation
            return nil
        }
        if let early {
            return early
        }

        let yes: Bool
        do {
            yes = try ask()
        } catch {
            // Cleared, or every phone after this one would be refused as if the question were still open.
            locked { asking = false }
            throw error
        }
        locked {
            asking = false
            if !yes {
                declined = (address, now() + TrustStore.quietAfterDeclineSeconds)
            }
        }
        if !yes {
            return false
        }

        return locked {
            if generation != askedIn {
                Log.write("Did not trust \(address): the trusted phones were forgotten while the owner was asked")
                return false
            }
            // Settled again after the wait: a file edited by hand in that minute is still the one that counts, and
            // a phone it already names is not written twice.
            if let settled = decided(address) {
                return settled
            }
            do {
                try save(tryRead().addresses + [address])
            } catch {
                // Admitted anyway, and remembered until the app quits. Refusing the owner's own phone because
                // the disk is full would be a worse answer than forgetting it at the next start.
                unsaved.append(address)
                Log.write("The trusted phone could not be saved to \(path): \(error.localizedDescription)")
            }
            return true
        }
    }

    /// Trusts `address` without asking: the owner already said yes another way, by typing a network phone's pairing
    /// code on this Mac. A trust file that cannot be read is left as it is, so the phone is refused at `admit` like
    /// every other until the owner forgets them; one that cannot be written keeps the phone for this run, as `admit`.
    public func grant(_ address: String) {
        locked {
            let read = tryRead()
            guard read.readable, !TrustStore.names(withUnsaved(read.addresses), address) else { return }
            do {
                try save(read.addresses + [address])
            } catch {
                unsaved.append(address)
                Log.write("The trusted phone could not be saved to \(path): \(error.localizedDescription)")
            }
        }
    }

    /// True when `address` is certain to be refused: the owner is being asked about a phone right now, or just
    /// declined this one, or the trust file cannot be read. A connection that arrives during the question is turned
    /// away without disturbing the phone being asked about, and may try again once the question is answered. A
    /// phone that is merely not trusted yet is not refused here: it is asked about. Checked before a new connection
    /// starts its handshake; unlike `admit` it never grants trust.
    public func refuses(_ address: String) -> Bool {
        locked { asking || isDeclined(address) || !tryRead().readable }
    }

    /// True when every phone is forgotten: the trust file is gone (deleted or never there) or, when it could not be
    /// deleted, emptied, which reads the same. False when neither worked: the phones are then still named on disk,
    /// so every phone is refused for this run, as for an unreadable file, until a later forget succeeds, and those
    /// phones would be trusted again after a restart.
    @discardableResult
    public func forget() -> Bool {
        locked {
            generation += 1
            unsaved = []
            declined = nil
            do {
                try FileManager.default.removeItem(atPath: path)
            } catch let deleteError {
                if !isAbsent(deleteError, at: path) {
                    // A blank file already means nothing is trusted, and emptying needs different rights from
                    // deleting. It is done in place: an atomic write would need the directory permission the
                    // delete just lacked.
                    do {
                        guard let handle = FileHandle(forWritingAtPath: path) else {
                            throw CocoaError(.fileWriteUnknown)
                        }
                        try handle.truncate(atOffset: 0)
                        try handle.close()
                        Log.write(
                            "The trusted phones' file could not be deleted at \(path) and was emptied instead: "
                                + deleteError.localizedDescription)
                    } catch {
                        Log.write(
                            "The trusted phones could not be forgotten at \(path), as it could not be emptied either: "
                                + deleteError.localizedDescription)
                        forgetFailed = true
                        return false
                    }
                }
            }
            forgetFailed = false
            return true
        }
    }

    private func locked<T>(_ body: () -> T) -> T {
        gate.lock()
        defer { gate.unlock() }
        return body()
    }

    /// Whether `address` is the phone declined within the quiet period. Called under the lock.
    private func isDeclined(_ address: String) -> Bool {
        guard let declined, now() < declined.until else { return false }
        return declined.address.caseInsensitiveCompare(address) == .orderedSame
    }

    /// Whether `address` is admitted without asking: true for a trusted phone, false for every phone while the
    /// trust file cannot be read, nil for a phone the owner has not said yes to. Called under the lock.
    private func decided(_ address: String) -> Bool? {
        let read = tryRead()
        if !read.readable {
            // Fail closed. The phone sees a refusal, the log says why, and Forget is the way out; the
            // alternative is a Mac that quietly re-pairs itself whenever its own trust file is unreadable,
            // which is the single thing this class exists to prevent.
            return false
        }
        return TrustStore.names(withUnsaved(read.addresses), address) ? true : nil
    }

    /// The addresses in the file, then those admitted this run that could not be saved. Called under the lock.
    private func withUnsaved(_ addresses: [String]) -> [String] {
        addresses + unsaved.filter { !TrustStore.names(addresses, $0) }
    }

    /// Addresses are compared without regard to case, as the phone and macOS may write them differently.
    private static func names(_ addresses: [String], _ address: String) -> Bool {
        addresses.contains { $0.caseInsensitiveCompare(address) == .orderedSame }
    }

    /// Readable is false when the file is there and could not be read: the caller must then refuse rather than
    /// guess. No addresses with readable true means nothing is trusted yet, which an absent file and a blank one
    /// both mean: Forget deletes the file, and a blank one is the same intent written by hand. A file from before
    /// there could be several phones holds one address, and is read as a list of one. A forget that could not
    /// delete the file is unreadable too, whatever the file now says.
    private func tryRead() -> (readable: Bool, addresses: [String]) {
        if forgetFailed { return (false, []) }
        do {
            let text = try String(contentsOfFile: path, encoding: .utf8)
            let lines = text.components(separatedBy: .newlines).map { $0.trimmingCharacters(in: .whitespaces) }
            return (true, lines.filter { !$0.isEmpty })
        } catch {
            if isAbsent(error, at: path) {
                return (true, [])
            }
            Log.write("The trusted phones could not be read from \(path): \(error.localizedDescription)")
            return (false, [])
        }
    }

    /// Rewrites the whole list. The directory is private to the user and so is the file: the addresses say which
    /// devices may drive this Mac.
    private func save(_ addresses: [String]) throws {
        let manager = FileManager.default
        let directory = (path as NSString).deletingLastPathComponent
        try manager.createDirectory(
            atPath: directory.isEmpty ? "." : directory,
            withIntermediateDirectories: true,
            attributes: [.posixPermissions: 0o700]
        )
        let data = Data((addresses.joined(separator: "\n") + "\n").utf8)
        guard manager.createFile(atPath: path, contents: data, attributes: [.posixPermissions: 0o600]) else {
            throw CocoaError(.fileWriteUnknown)
        }
    }
}
