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

    /// Runs at each refresh, so a test can act in the middle of the opening report.
    var onRefresh: (() -> Void)?

    func refresh() {
        onRefresh?()
    }

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

final class FakePower: PowerSource {
    var list: [BatteryEntry] = []
    var out: (OutputKind, String)?
    var changed: (() -> Void)?

    func batteries() -> [BatteryEntry] { list }
    func output() -> (OutputKind, String)? { out }
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

final class FakeMedia: MediaSource {
    var info = MediaInfo.none
    var changed: (() -> Void)?
    var seeks: [Int] = []
    var iconApps: [String] = []

    func read() -> MediaInfo { info }

    func seek(percent: Int) -> Bool {
        seeks.append(percent)
        return true
    }

    func icon(forApp app: String) -> [UInt8]? {
        iconApps.append(app)
        return nil
    }
}

/// A source of the app in front that keeps the callback so a test can fire a change by hand.
final class FakeForeground: ForegroundSource {
    var available = true
    private(set) var watches = 0
    private(set) var cancels = 0
    private var onChange: ((String) -> Void)?

    func watch(_ onChange: @escaping (String) -> Void) -> ForegroundWatch {
        watches += 1
        self.onChange = onChange
        onChange("fake.app/Fake")
        return ForegroundWatch { [weak self] in
            self?.cancels += 1
            self?.onChange = nil
        }
    }

    /// A change as the platform would report it; nothing happens once the watch is cancelled.
    func change(to text: String) {
        onChange?(text)
    }
}

/// A power platform that records what it was asked and lets a test end the countdown by hand.
final class FakePowerPlatform: PowerPlatform {
    var caps: PowerCaps = [.sleep, .hibernate, .restart, .shutDown]
    private(set) var slept = 0
    private(set) var hibernated = 0
    private(set) var executed: [PowerKind] = []
    private(set) var started: [PowerKind] = []
    var pending: (kind: PowerKind, secondsLeft: Int)?
    private var ended: ((Bool) -> Void)?

    func sleep() -> Bool {
        slept += 1
        return true
    }

    func hibernate() -> Bool {
        hibernated += 1
        return true
    }

    func startCountdown(_ kind: PowerKind, seconds: Int, ended: @escaping (Bool) -> Void) -> Bool {
        started.append(kind)
        pending = (kind, seconds)
        self.ended = ended
        return true
    }

    func cancelCountdown() {
        finish(false)
    }

    func execute(_ kind: PowerKind) -> Bool {
        executed.append(kind)
        return true
    }

    /// The countdown reaching zero (true) or the window closing (false).
    func finish(_ fired: Bool) {
        guard let ended else { return }
        self.ended = nil
        pending = nil
        ended(fired)
    }
}
