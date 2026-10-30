@testable import EdgepadCore

final class FakeInput: InputSink {
    private(set) var calls: [String] = []
    private var held: Set<MacKey> = []

    func move(dx: Int, dy: Int) {
        calls.append("move \(dx) \(dy)")
    }

    func button(_ id: UInt8, down: Bool) {
        calls.append("button \(id) \(down)")
    }

    func scroll(dx: Int, dy: Int) {
        calls.append("scroll \(dx) \(dy)")
    }

    func type(_ text: String) {
        calls.append("type \(text)")
    }

    func key(_ key: MacKey, down: Bool) {
        calls.append("key \(key.code) \(down)")
        if down {
            held.insert(key)
        } else {
            held.remove(key)
        }
    }

    func chord(_ keys: [MacKey]) {
        calls.append("chord " + keys.map { String($0.code) }.joined(separator: ","))
    }

    func hold(_ key: MacKey) {
        calls.append("hold \(key.code)")
        held.insert(key)
    }

    func release(_ key: MacKey) {
        calls.append("release \(key.code)")
        held.remove(key)
    }

    func isHeld(_ key: MacKey) -> Bool {
        held.contains(key)
    }

    func media(_ key: MediaKey) {
        calls.append("media \(key.rawValue)")
    }

    func releaseAll() {
        calls.append("releaseAll")
        held.removeAll()
    }
}

final class FakeLevel: LevelControl {
    var level: Level?
    var accepts = true
    private(set) var sets: [Int] = []
    private(set) var muteToggles = 0

    init(level: Level? = nil) {
        self.level = level
    }

    func read() -> Level? {
        level
    }

    func setLevel(_ percent: Int) -> Bool {
        sets.append(percent)
        return accepts
    }

    func toggleMute() -> Bool {
        muteToggles += 1
        return accepts
    }
}

final class FakeBrightness: BrightnessControl {
    var level: Int?
    var accepts = true
    private(set) var sets: [Int] = []

    init(level: Int? = nil) {
        self.level = level
    }

    func read() -> Int? {
        level
    }

    func set(_ percent: Int) -> Bool {
        sets.append(percent)
        return accepts
    }
}

final class FakeDisplay: DisplayRates {
    var rates: [Int]
    var current: Int?
    private(set) var selected: [Int] = []

    init(rates: [Int] = [], current: Int? = nil) {
        self.rates = rates
        self.current = current
    }

    func refresh() {}

    func select(_ index: Int) -> Bool {
        selected.append(index)
        return true
    }
}

final class FakeTransport: Transport {
    private(set) var sent: [[UInt8]] = []
    var alive = true
    private(set) var closeCount = 0
    /// What the link still holds, which a test clears to stand for the link draining.
    var backlog = 0
    /// When set, the link fails a send that would take `backlog` past this, as the real transports' outboxes do.
    var cap: Int?

    var closed: Bool {
        closeCount > 0
    }

    func send(_ bytes: [UInt8]) -> Bool {
        guard alive else { return false }
        if let cap {
            guard backlog + bytes.count <= cap else { return false }
            backlog += bytes.count
        }
        sent.append(bytes)
        return true
    }

    func close() {
        closeCount += 1
    }

    /// Everything sent so far, decoded the way the phone would read it.
    func frames() -> [Frame] {
        var reader = FrameReader()
        return (try? reader.append(sent.flatMap { $0 })) ?? []
    }
}

final class FakeAnnotation: AnnotationSink {
    var screenSize: (width: Int, height: Int)? = (1920, 1080)
    var signStatusReply: ((String) -> Void)?
    var screenSizeChanged: (() -> Void)?
    var accepts = true
    private(set) var calls: [String] = []
    private(set) var endCount = 0

    func pen(x: UInt16, y: UInt16, state: UInt8) -> Bool {
        calls.append("pen \(x) \(y) \(state)")
        return accepts
    }

    func setColour(_ index: Int) -> Bool {
        calls.append("colour \(index)")
        return accepts
    }

    func setRgb(_ rgb: UInt32) -> Bool {
        calls.append("rgb \(rgb)")
        return accepts
    }

    func setWidth(_ width: Int) -> Bool {
        calls.append("width \(width)")
        return accepts
    }

    func setOpacity(_ opacity: Int) -> Bool {
        calls.append("opacity \(opacity)")
        return accepts
    }

    func run(_ action: ActionId) -> Bool {
        calls.append("run \(action.rawValue)")
        return accepts
    }

    func end() {
        endCount += 1
    }
}

final class FakeLauncher: MacroLauncher {
    private(set) var launched: [String] = []
    var accepts = true

    func launch(target: String, arguments: String?) -> Bool {
        launched.append(target + "|" + (arguments ?? "-"))
        return accepts
    }
}

/// A desktop that writes each action down and answers whatever the test set.
final class FakeWindows: MacroWindows {
    var available = true
    var acts = true
    var state: (Macro) -> OpenState = { _ in .closed }
    private(set) var calls: [String] = []
    private(set) var surveys = 0

    func survey(_ macros: [Macro]) -> [OpenState] {
        surveys += 1
        return macros.map(state)
    }

    func minimize(_ macro: Macro) -> Bool {
        record("minimize \(macro.name)")
    }

    func raise(_ macro: Macro) -> Bool {
        record("raise \(macro.name)")
    }

    func close(_ macro: Macro) -> Bool {
        record("close \(macro.name)")
    }

    private func record(_ call: String) -> Bool {
        calls.append(call)
        return acts
    }
}

final class FakeIcons: MacroIconSource {
    var bytes: [UInt8]?

    init(bytes: [UInt8]? = nil) {
        self.bytes = bytes
    }

    /// The sizes asked for, in order.
    private(set) var sizes: [Int] = []

    func png(for macro: Macro, size: Int) -> [UInt8]? {
        sizes.append(size)
        return bytes
    }
}

/// A catalogue in memory, which answers what the test set.
final class FakeCatalog: AppCatalog {
    struct Gone: Error {}

    var apps: [AppEntry] = []
    var scanThrows = false
    /// What `icon(for:)` answers; a test sets it to return a picture, return nothing or throw.
    var iconFor: (AppEntry) throws -> [UInt8]? = { _ in nil }
    private(set) var scans = 0

    func scan() throws -> [AppEntry] {
        scans += 1
        if scanThrows {
            throw Gone()
        }
        return apps
    }

    func icon(for app: AppEntry) throws -> [UInt8]? {
        try iconFor(app)
    }
}

/// Folders in memory: a path maps to what is directly inside it, and a path with no entry is one that is not there.
final class FakeFolders: AppFolders {
    var tree: [String: [AppFolderEntry]]
    /// Every path asked for, in order.
    private(set) var reads: [String] = []

    init(_ tree: [String: [AppFolderEntry]] = [:]) {
        self.tree = tree
    }

    func entries(in path: String) -> [AppFolderEntry]? {
        reads.append(path)
        return tree[path]
    }
}
