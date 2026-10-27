import Foundation

/// One launcher button. `target` is anything the system can open, and `arguments` is nil when there are none, so a
/// macro with none is not a macro with an empty command line. `icon` overrides the picture the phone shows; nil
/// means take it from the target.
public struct Macro: Equatable {
    public let name: String
    public let target: String
    public let arguments: String?
    public let icon: String?

    public init(name: String, target: String, arguments: String? = nil, icon: String? = nil) {
        self.name = name
        self.target = target
        self.arguments = arguments
        self.icon = icon
    }
}

/// The launcher's action table, and the only thing that knows what a macro button means; the twin of the Windows
/// `MacroStore`.
///
/// The founding rule of this project is that gestures are recognised on the phone and the computer only executes:
/// the phone sends semantic frames, never an arbitrary key or command line, and the computer owns the action table.
/// A launcher is where that rule is easiest to break and worst to break. So the phone sends an index and nothing
/// else: `run` takes an `Int`, and there is deliberately no overload taking a path, a URL or a command line. No
/// string that arrives over Bluetooth ever reaches the launcher. The only way a target gets into this list is the
/// owner putting it there, on this Mac, in front of the machine it will run on.
///
/// What that buys, stated honestly: a macro button cannot be repointed from the phone. A slot runs what the owner
/// put in it or nothing, and the protocol stays semantic. Do not "simplify" it by letting the phone name what it
/// wants to launch. What it does NOT buy is containment of a compromised phone: a paired phone already types
/// anything through KEY and TEXT, so it is a trusted input device, and the trust boundary is the pairing, not this
/// list.
///
/// The file is plain tab-separated text: one macro per line, name, target, arguments, and an icon path only when
/// there is one. Not JSON: a format a human can fix in a text editor is worth more here than one a parser likes.
///
/// Reads fail closed: a file that cannot be read gives an empty list, never a guess.
public final class MacroStore {
    /// A 5x3 grid on the phone. Fifteen is also exactly what one TEXT frame can name: 15 x `maxNameBytes` plus 14
    /// separators is 254 of the 255 a payload holds, so a full list always reaches the phone labelled. The number
    /// is not a coincidence and must not be raised without cutting the name budget to match.
    public static let maxMacros = 15

    /// A name's budget in UTF-8 bytes rather than characters. Bytes are what the frame's single length byte
    /// counts, and sixteen accented or emoji characters weigh far more than sixteen letters, so a cap in
    /// characters would let a legal-looking name overflow the list and silently lose a label.
    public static let maxNameBytes = 16

    /// A TEXT payload's length is a single byte, so 255 UTF-8 bytes is the hard ceiling.
    public static let maxNamesBytes = 255

    private let path: String
    private let launcher: MacroLauncher?
    private let icons: MacroIconSource?
    private let gate = NSLock()
    private var watchers: [Int: (String) -> Void] = [:]
    private var nextWatcher = 0
    private var list: [Macro] = []
    private var changes = 0

    /// `launcher` and `icons` are nil in the core's own tests; the app supplies both. Without a launcher `run`
    /// starts nothing and says so.
    public init(path: String, launcher: MacroLauncher? = nil, icons: MacroIconSource? = nil) {
        self.path = path
        self.launcher = launcher
        self.icons = icons
        list = MacroStore.read(path)
    }

    public static func forCurrentUser(launcher: MacroLauncher?, icons: MacroIconSource?) -> MacroStore {
        let support =
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Library/Application Support"
        return MacroStore(path: support + "/Edgepad/macros.txt", launcher: launcher, icons: icons)
    }

    /// Where the macros are kept, for a message that has to send the owner there.
    public var filePath: String { path }

    /// The macros in order. The index is what the phone names. Never longer than `maxMacros`.
    public var macros: [Macro] { locked { list } }

    /// Moves on with every list swap. A sender that spans several frames compares it before each one, so no frame
    /// of the old list can follow the new one to the phone, where it would land on whatever moved into its slot.
    public var revision: Int { locked { changes } }

    /// Re-reads from disk, and tells anyone watching what came back.
    public func reload() {
        publish(replacing: MacroStore.read(path))
    }

    /// Nil once the list is written and published, otherwise why the write failed. On a failure the list in use is
    /// left as it was, so what the phone shows is what is on disk and the owner, told by the caller, can retry. The
    /// Windows store keeps the new list for the session instead; here the owner is told, so nothing is silently lost.
    @discardableResult
    public func save(_ macros: [Macro]) -> String? {
        let clean = MacroStore.clean(macros)
        let text = clean.map { MacroStore.line($0) + "\n" }.joined()
        if let failure = writePrivately(text, to: path) {
            Log.write("Macros could not be saved to \(path): \(failure)")
            return failure
        }
        publish(replacing: clean)
        return nil
    }

    /// Reports the label list now and whenever it changes, until the returned token is cancelled. A session
    /// subscribes instead of asking once at the handshake: the owner adds a macro and expects the button on the
    /// phone, not a reason to restart the app.
    public func watch(_ onChange: @escaping (String) -> Void) -> MacroWatch {
        let id = locked { () -> Int in
            nextWatcher += 1
            watchers[nextWatcher] = onChange
            return nextWatcher
        }
        onChange(names())
        return MacroWatch { [weak self] in
            self?.locked { _ = self?.watchers.removeValue(forKey: id) }
        }
    }

    /// Launches `macros[index]`. False when the index is out of range, there is no launcher, or the launch failed.
    /// An index and nothing else: see the type's comment.
    public func run(_ index: Int) -> Bool {
        let current = macros
        // A phone with a longer list than this Mac has, an editor open on the Mac say, is not an error, just a
        // button that does nothing. The dispatcher counts it and carries on.
        guard current.indices.contains(index), let launcher else { return false }
        let macro = current[index]
        guard launcher.launch(target: macro.target, arguments: macro.arguments) else {
            // A target that has been moved, deleted or renamed since it was added is the ordinary case.
            Log.write("Macro \(index) (\(macro.name)) could not start \(macro.target)")
            return false
        }
        return true
    }

    /// The picture for `macros[index]`, as PNG bytes, if the app can supply one small enough to send.
    public func icon(at index: Int) -> [UInt8]? {
        let current = macros
        guard current.indices.contains(index), let png = icons?.png(for: current[index]) else { return nil }
        return png.count <= MacroIconChunks.maxBytes ? png : nil
    }

    /// The names, in order, for sending to the phone.
    public func names() -> String {
        var text = ""
        var bytes = 0
        for macro in macros {
            // Whole names only, counted in UTF-8 bytes: the length byte counts encoded bytes, and a frame that
            // stopped mid-character would be a protocol error, not a short list.
            let size = macro.name.utf8.count + (text.isEmpty ? 0 : 1)
            if bytes + size > MacroStore.maxNamesBytes {
                break
            }
            if !text.isEmpty {
                text += "/"
            }
            text += macro.name
            bytes += size
        }
        return text
    }

    /// The index of the first macro with nothing to open, or -1 when every one has a target. `save` drops such a
    /// row, so an editor asks with this first and points the owner at it, rather than letting a macro they filled
    /// in disappear on the way to the phone.
    public static func withoutTarget(_ macros: [Macro]) -> Int {
        macros.firstIndex { plain($0.target).isEmpty } ?? -1
    }

    /// One macro as a line of the file. The icon field is left off entirely when there is none, rather than
    /// written as a trailing tab, so a list with no icons has the same bytes the Windows build writes.
    private static func line(_ macro: Macro) -> String {
        let base = "\(macro.name)\t\(macro.target)\t\(macro.arguments ?? "")"
        return macro.icon.map { base + "\t" + $0 } ?? base
    }

    /// What will be written and what the phone will be told, from what an editor collected. Everything that
    /// reaches the list goes through here, whether it came from an editor or from a file edited by hand.
    private static func clean(_ macros: [Macro]) -> [Macro] {
        var clean: [Macro] = []
        for macro in macros {
            let target = plain(macro.target)
            if target.isEmpty {
                // A row with nothing to launch would still take an index, and every macro after it would answer
                // to a different button than the one the owner put it on.
                continue
            }
            let arguments = plain(macro.arguments ?? "")
            let icon = plain(macro.icon ?? "")
            let cleaned = Macro(
                name: name(macro.name),
                target: target,
                arguments: arguments.isEmpty ? nil : arguments,
                icon: icon.isEmpty ? nil : icon)
            clean.append(cleaned)
            if clean.count == maxMacros {
                break
            }
        }
        return clean
    }

    /// A name the joined list can survive. "/" separates the names in the TEXT frame and a tab separates the
    /// fields on disk, so neither can appear inside one; the phone would read one name as two.
    private static func name(_ name: String) -> String {
        let flat = plain(name.replacingOccurrences(of: "/", with: "-"))
        let cut = cutToBytes(flat, maxNameBytes).trimmingCharacters(in: .whitespacesAndNewlines)
        // A file edited by hand may carry no name, and a button with no label is worse than a dull one.
        return cut.isEmpty ? "Macro" : cut
    }

    /// The longest prefix of whole Unicode scalars whose UTF-8 fits `limit`; never between the halves of a
    /// character, which would go out as a replacement character.
    private static func cutToBytes(_ value: String, _ limit: Int) -> String {
        var kept = String.UnicodeScalarView()
        var bytes = 0
        for scalar in value.unicodeScalars {
            let width = String(scalar).utf8.count
            if bytes + width > limit {
                break
            }
            bytes += width
            kept.append(scalar)
        }
        return String(kept)
    }

    /// Whatever would corrupt a tab-separated line, tabs, newlines and the rest of the control characters, becomes
    /// a space. A space rather than nothing, because dropping the tab out of "foo<tab>bar" would run the two sides
    /// together into a word that was never in either of them; trailing ones come off in the trim.
    private static func plain(_ value: String) -> String {
        var plain = String.UnicodeScalarView()
        for scalar in value.unicodeScalars {
            plain.append(scalar.properties.generalCategory == .control ? " " : scalar)
        }
        return String(plain).trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// Takes the new list and hands it to every watcher, outside the lock: a watcher writes to the link.
    private func publish(replacing new: [Macro]) {
        let targets = locked { () -> [(String) -> Void] in
            list = new
            changes += 1
            return Array(watchers.values)
        }
        let names = names()
        for target in targets {
            target(names)
        }
    }

    private func locked<T>(_ body: () -> T) -> T {
        gate.lock()
        defer { gate.unlock() }
        return body()
    }

    private static func read(_ path: String) -> [Macro] {
        let text: String
        do {
            text = try String(contentsOfFile: path, encoding: .utf8)
        } catch {
            if !isAbsent(error, at: path) {
                Log.write("Macros could not be read from \(path): \(error.localizedDescription)")
            }
            return []
        }

        var read: [Macro] = []
        for line in text.split(omittingEmptySubsequences: false, whereSeparator: { $0.isNewline }) {
            let fields = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
            guard fields.count >= 2 else { continue }
            let arguments = fields.count > 2 ? fields[2] : nil
            let icon = fields.count > 3 ? fields[3] : nil
            read.append(Macro(name: fields[0], target: fields[1], arguments: arguments, icon: icon))
        }
        // Through `clean` rather than straight out, so a hand-edited file is held to the same rules as an
        // editor's output, including the cap: a file with a thousand lines cannot become a thousand macros.
        return clean(read)
    }
}

/// A subscription to the label list. Cancelling is idempotent, and a session cancels in `end()`.
public final class MacroWatch {
    private var onCancel: (() -> Void)?

    fileprivate init(_ onCancel: @escaping () -> Void) {
        self.onCancel = onCancel
    }

    public func cancel() {
        onCancel?()
        onCancel = nil
    }
}

/// Splits one icon into TEXT 6 payloads, "slot/chunk/chunks/base64", the twin of the laptop core's `MacroIconChunks.Chunks`.
public enum MacroIconChunks {
    /// The most an icon may weigh on the wire: 24 KiB is 137 frames for one button, the point past which a picture
    /// costs the link more than a label is worth. A real program icon is a fraction of it.
    public static let maxBytes = 24 * 1024

    /// How much base64 rides in one frame. A payload is 255 bytes and the rest is the "slot/chunk/chunks/" header:
    /// two digits of slot and three of chunk count at most is eleven characters, so 240 leaves four to spare. If
    /// the count or this number moves, check them together; a header that outgrew the margin costs a truncated icon.
    public static let chunkBytes = 240

    /// Empty for no bytes, rather than one empty frame, which would reach the phone as a finished icon of nothing.
    public static func chunks(slot: Int, png: [UInt8]) -> [String] {
        let encoded = Array(Data(png).base64EncodedString())
        let count = (encoded.count + chunkBytes - 1) / chunkBytes
        return (0..<count).map { chunk in
            let start = chunk * chunkBytes
            let end = min(start + chunkBytes, encoded.count)
            return "\(slot)/\(chunk)/\(count)/\(String(encoded[start..<end]))"
        }
    }
}
