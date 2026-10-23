import CommonCrypto
import Foundation
import Security

/// What `PinStore.check` made of one answer.
public enum PinResult: Equatable {
    case accepted
    /// Wrong, with this many tries left before the phone is locked out.
    case wrong(attemptsLeft: Int)
    /// Locked out for this many seconds; the answer was not even looked at.
    case locked(seconds: Int)
    /// No answer can be judged: the settings cannot be read, there is no PIN, or the PIN or level changed while
    /// this one was being hashed. Never a reason to let the phone in.
    case unavailable
}

/// The Mac's security level and the PIN behind levels 2 and 3, the twin of the Windows `PinStore`. Level 1 is trust
/// only, as before there was a PIN; level 2 asks each phone for the PIN once and remembers it in `pin-verified.txt`;
/// level 3 asks on every connection. The PIN itself is never stored: `security.txt` holds a salted PBKDF2 hash. That
/// hash does not stop someone who copies the file, since six digits are only 10^6 guesses offline; what stops
/// guessing is the lockout below and the files being private to the owner.
///
/// Reading mirrors `TrustStore`: a settings file that is there and cannot be read, or holds something Edgepad did
/// not write, is not "no PIN". It makes every phone need a PIN that cannot be checked, so every phone is refused
/// until the owner uses Reset PIN. A missing file is level 1 with no PIN, the state before this existed.
///
/// Wrong answers are counted per phone in memory only: quitting the app clears them, and doing that already needs
/// the owner's desktop, the very thing the PIN stands in for.
public final class PinStore {
    /// PBKDF2-HMAC-SHA256 work factor, from OWASP's Password Storage Cheat Sheet
    /// (https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html), the figure the Windows
    /// store uses. Each stored hash carries its own count, so raising this later re-hashes nothing and breaks
    /// nothing; the next Set PIN uses the new figure.
    public static let defaultIterations: UInt32 = 600_000

    private static let scheme = "pbkdf2-sha256"
    private static let saltBytes = 16
    private static let hashBytes = 32

    // The lockout schedule from the protocol: every fifth wrong answer locks the phone out, for 30 s after the first
    // five, doubling with each further five, never more than an hour.
    private static let attemptsPerBatch = 5
    private static let firstLockSeconds = 30
    private static let maxLockSeconds = 3600

    private let securityPath: String
    private let verifiedPath: String
    private let iterations: UInt32
    private let gate = NSLock()

    /// Uptime, not the wall clock, so a clock stepped backwards cannot stretch or shorten a lockout. A test hands in
    /// its own.
    private let now: () -> TimeInterval

    /// Per lowercased address: wrong answers so far, and the uptime until which it is locked out. A success removes
    /// the entry.
    private var failures: [String: (count: Int, until: TimeInterval)] = [:]

    /// Moved on by every change of PIN or level. A check hashed against the old settings proves nothing about the
    /// new ones, and must not put a phone on a verified list that the change just cleared.
    private var generation = 0

    public init(
        directory: String,
        now: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime },
        iterations: UInt32 = PinStore.defaultIterations
    ) {
        securityPath = directory + "/security.txt"
        verifiedPath = directory + "/pin-verified.txt"
        self.now = now
        self.iterations = iterations
    }

    public static func forCurrentUser() -> PinStore {
        let support =
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Library/Application Support"
        return PinStore(directory: support + "/Edgepad")
    }

    /// The security level, 1 to 3. 3 while the settings cannot be read, which is how they are enforced then.
    public var level: Int {
        locked { tryRead()?.level ?? 3 }
    }

    /// True when a PIN is set, and while the settings cannot be read: an unreadable PIN is not an absent one.
    public var hasPin: Bool {
        locked { tryRead().map { $0.pin != nil } ?? true }
    }

    /// Sets a new PIN with a new salt and empties the verified list, so every level-2 phone enters the new PIN once:
    /// a changed secret should not be honoured by a proof of the old one. False when it could not be saved, or the
    /// settings cannot be read (Reset is the way out of that).
    public func setPin(_ pin: Pin) -> Bool {
        // Hundreds of milliseconds at the real work factor, so never under the lock: the menu reads `level` on the
        // main thread, and a connection's check would otherwise queue behind it.
        guard let hash = PinStore.hash(pin, iterations: iterations) else {
            Log.write("The PIN could not be hashed, so it was not changed")
            return false
        }
        return locked {
            guard let read = tryRead(), delete(verifiedPath) else { return false }
            generation += 1
            return save(level: read.level, pin: hash)
        }
    }

    /// Sets the level and empties the verified list. Levels 2 and 3 are refused without a PIN, which would lock every
    /// phone out with no way to answer. False when refused or not saved.
    public func setLevel(_ level: Int) -> Bool {
        guard (1...3).contains(level) else { return false }
        return locked {
            guard let read = tryRead(), level == 1 || read.pin != nil, delete(verifiedPath) else { return false }
            generation += 1
            return save(level: level, pin: read.pin)
        }
    }

    /// Whether `address` must answer the PIN before it is admitted. Always true while the settings cannot be read.
    public func required(_ address: String) -> Bool {
        locked {
            guard let read = tryRead() else { return true }
            switch read.level {
            case 1:
                return false
            case 2:
                return !readVerified().contains { $0.caseInsensitiveCompare(address) == .orderedSame }
            default:
                return true
            }
        }
    }

    /// How much longer `address` is locked out, in seconds, or nil when it is not.
    public func lockedFor(_ address: String) -> TimeInterval? {
        locked { lockedTime(address) }
    }

    /// Tries left before the next lockout; 0 while locked out.
    public func attemptsLeft(_ address: String) -> Int {
        locked {
            if lockedTime(address) != nil {
                return 0
            }
            let count = failures[address.lowercased()]?.count ?? 0
            return PinStore.attemptsPerBatch - count % PinStore.attemptsPerBatch
        }
    }

    /// Judges one answer from `address`. Right: its failures are forgotten and, at level 2, it is added to the
    /// verified list. Wrong: counted, and every fifth one locks it out.
    public func check(_ address: String, _ pin: Pin) -> PinResult {
        var refusal = PinResult.unavailable
        let start: (stored: String, level: Int, generation: Int)? = locked {
            guard let read = tryRead(), let hash = read.pin else { return nil }
            if let wait = lockedTime(address) {
                refusal = .locked(seconds: PinStore.seconds(wait))
                return nil
            }
            return (hash, read.level, generation)
        }
        guard let start else { return refusal }

        // Outside the lock for the same reason as in setPin.
        let right = PinStore.matches(start.stored, pin)

        return locked {
            if generation != start.generation {
                return .unavailable
            }
            // Another connection from the same address may have used up the attempts while this one hashed.
            if let wait = lockedTime(address) {
                return .locked(seconds: PinStore.seconds(wait))
            }

            let key = address.lowercased()
            if right {
                failures[key] = nil
                if start.level == 2 {
                    remember(address)
                }
                return .accepted
            }

            let count = (failures[key]?.count ?? 0) + 1
            if count % PinStore.attemptsPerBatch != 0 {
                failures[key] = (count, 0)
                return .wrong(attemptsLeft: PinStore.attemptsPerBatch - count % PinStore.attemptsPerBatch)
            }
            let seconds = PinStore.lockSeconds(batches: count / PinStore.attemptsPerBatch)
            failures[key] = (count, now() + TimeInterval(seconds))
            return .locked(seconds: seconds)
        }
    }

    /// Forgets the PIN and every verified phone and goes back to level 1, for an owner who forgot the PIN. The caller
    /// also forgets the trusted phones, so the way back in is the owner's own yes on this Mac. False when a file could
    /// not be deleted; the old settings then still apply.
    public func reset() -> Bool {
        locked {
            generation += 1
            failures = [:]
            let verifiedGone = delete(verifiedPath)
            return delete(securityPath) && verifiedGone
        }
    }

    private func locked<T>(_ body: () -> T) -> T {
        gate.lock()
        defer { gate.unlock() }
        return body()
    }

    /// Seconds until `address` may answer again, or nil. Called under the lock.
    private func lockedTime(_ address: String) -> TimeInterval? {
        let at = now()
        guard let entry = failures[address.lowercased()], entry.until > at else { return nil }
        return entry.until - at
    }

    // Rounded up, so a phone told "1 s" is never early.
    private static func seconds(_ wait: TimeInterval) -> Int {
        Int(wait.rounded(.up))
    }

    // Capped before shifting: 30 << 7 is already past the hour.
    private static func lockSeconds(batches: Int) -> Int {
        batches > 7 ? maxLockSeconds : min(firstLockSeconds << (batches - 1), maxLockSeconds)
    }

    /// `pbkdf2-sha256$<iterations>$<saltB64>$<hashB64>`, or nil when no random salt could be had.
    private static func hash(_ pin: Pin, iterations: UInt32) -> String? {
        var salt = [UInt8](repeating: 0, count: saltBytes)
        guard SecRandomCopyBytes(kSecRandomDefault, saltBytes, &salt) == errSecSuccess,
            let hash = derive(pin, salt: salt, iterations: iterations, length: hashBytes)
        else { return nil }
        return "\(scheme)$\(iterations)$\(Data(salt).base64EncodedString())$\(Data(hash).base64EncodedString())"
    }

    private static func matches(_ stored: String, _ pin: Pin) -> Bool {
        guard let parsed = parse(stored),
            let actual = derive(pin, salt: parsed.salt, iterations: parsed.iterations, length: parsed.hash.count)
        else { return false }
        return equalInConstantTime(actual, parsed.hash)
    }

    private static func derive(_ pin: Pin, salt: [UInt8], iterations: UInt32, length: Int) -> [UInt8]? {
        // The digits as ASCII text, which is what Windows hashes, so a stored line means the same on either laptop.
        let password = pin.digits.map { CChar(bitPattern: $0 + 0x30) }
        var derived = [UInt8](repeating: 0, count: length)
        let status = CCKeyDerivationPBKDF(
            CCPBKDFAlgorithm(kCCPBKDF2),
            password,
            password.count,
            salt,
            salt.count,
            CCPseudoRandomAlgorithm(kCCPRFHmacAlgSHA256),
            iterations,
            &derived,
            length
        )
        return status == Int32(kCCSuccess) ? derived : nil
    }

    /// Looks at every byte whatever it finds, so how long a wrong answer takes says nothing about how wrong it was.
    private static func equalInConstantTime(_ a: [UInt8], _ b: [UInt8]) -> Bool {
        guard a.count == b.count else { return false }
        var difference: UInt8 = 0
        for index in a.indices {
            difference |= a[index] ^ b[index]
        }
        return difference == 0
    }

    /// Splits `pbkdf2-sha256$<iterations>$<saltB64>$<hashB64>`; nil for anything else.
    private static func parse(_ stored: String) -> (iterations: UInt32, salt: [UInt8], hash: [UInt8])? {
        let parts = stored.split(separator: "$", omittingEmptySubsequences: false)
        guard parts.count == 4, parts[0] == scheme, isDigits(parts[1]),
            let iterations = UInt32(parts[1]), iterations >= 1,
            let salt = Data(base64Encoded: String(parts[2])), salt.count == saltBytes,
            let hash = Data(base64Encoded: String(parts[3])), hash.count == hashBytes
        else { return nil }
        return (iterations, [UInt8](salt), [UInt8](hash))
    }

    // Swift's integer parsing takes a leading sign, which Edgepad never writes.
    private static func isDigits<S: StringProtocol>(_ text: S) -> Bool {
        !text.isEmpty && text.allSatisfy { $0 >= "0" && $0 <= "9" }
    }

    private static func lines(_ text: String) -> [String] {
        text.components(separatedBy: .newlines).map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
    }

    /// Nil when `security.txt` is there and cannot be read, or holds anything Edgepad would not have written (an
    /// unknown line, a level outside 1-3, a malformed hash, level 2 or 3 with no PIN); the caller must then refuse.
    /// Level 1 and no PIN for a missing file. Called under the lock.
    private func tryRead() -> (level: Int, pin: String?)? {
        let text: String
        do {
            text = try String(contentsOfFile: securityPath, encoding: .utf8)
        } catch {
            if PinStore.isAbsent(error, at: securityPath) {
                return (1, nil)
            }
            Log.write(
                "The security settings could not be read from \(securityPath), so every phone is refused: "
                    + error.localizedDescription)
            return nil
        }

        var level = 1
        var pin: String?
        for line in PinStore.lines(text) {
            let pair = line.split(separator: "=", maxSplits: 1, omittingEmptySubsequences: false)
                .map { $0.trimmingCharacters(in: .whitespaces) }
            guard pair.count == 2 else { return malformed() }
            switch pair[0] {
            case "level":
                guard PinStore.isDigits(pair[1]), let value = Int(pair[1]), (1...3).contains(value) else {
                    return malformed()
                }
                level = value
            case "pin":
                guard PinStore.parse(pair[1]) != nil else { return malformed() }
                pin = pair[1]
            default:
                return malformed()
            }
        }
        if level != 1 && pin == nil {
            return malformed()
        }
        return (level, pin)
    }

    // The file's content is never logged: it holds the hash.
    private func malformed() -> (level: Int, pin: String?)? {
        Log.write(
            "The security settings in \(securityPath) are not in a form Edgepad writes, so every phone is refused "
                + "until the PIN is reset")
        return nil
    }

    /// The level-2 phones that have entered the PIN. One that cannot be read is an empty list: those phones are asked
    /// again, which is the safe direction. Called under the lock.
    private func readVerified() -> [String] {
        do {
            return PinStore.lines(try String(contentsOfFile: verifiedPath, encoding: .utf8))
        } catch {
            if !PinStore.isAbsent(error, at: verifiedPath) {
                Log.write(
                    "The PIN-verified phones could not be read from \(verifiedPath): \(error.localizedDescription)")
            }
            return []
        }
    }

    // A phone that cannot be written down is simply asked for the PIN again next time. Called under the lock.
    private func remember(_ address: String) {
        let verified = readVerified()
        if !verified.contains(where: { $0.caseInsensitiveCompare(address) == .orderedSame }) {
            _ = write(verifiedPath, (verified + [address]).joined(separator: "\n") + "\n")
        }
    }

    private func save(level: Int, pin: String?) -> Bool {
        write(securityPath, pin.map { "level=\(level)\npin=\($0)\n" } ?? "level=\(level)\n")
    }

    /// Writes beside the file and renames it over, so a crash or a full disk mid-write leaves the old file whole
    /// rather than a half-written one that would read as malformed and refuse every phone. The directory is private
    /// to the user and so is the file, as for the trusted phones.
    private func write(_ path: String, _ text: String) -> Bool {
        let manager = FileManager.default
        let temp = path + ".tmp"
        do {
            try manager.createDirectory(
                atPath: (path as NSString).deletingLastPathComponent,
                withIntermediateDirectories: true,
                attributes: [.posixPermissions: 0o700]
            )
        } catch {
            Log.write("Could not save \(path): \(error.localizedDescription)")
            return false
        }
        let data = Data(text.utf8)
        guard manager.createFile(atPath: temp, contents: data, attributes: [.posixPermissions: 0o600]) else {
            Log.write("Could not save \(path): the file could not be written")
            return false
        }
        guard rename(temp, path) == 0 else {
            Log.write("Could not save \(path): \(String(cString: strerror(errno)))")
            try? manager.removeItem(atPath: temp)
            return false
        }
        return true
    }

    private func delete(_ path: String) -> Bool {
        do {
            try FileManager.default.removeItem(atPath: path)
            return true
        } catch {
            if PinStore.isAbsent(error, at: path) {
                return true
            }
            Log.write("Could not delete \(path): \(error.localizedDescription)")
            return false
        }
    }

    /// Whether `error` says the file is genuinely not there, as opposed to there and unreadable; the same test as
    /// `TrustStore`'s. The thrown error decides first; stat settles what Foundation reports vaguely.
    private static func isAbsent(_ error: Error, at path: String) -> Bool {
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
}
