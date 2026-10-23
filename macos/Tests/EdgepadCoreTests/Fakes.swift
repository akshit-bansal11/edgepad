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

    var closed: Bool {
        closeCount > 0
    }

    func send(_ bytes: [UInt8]) -> Bool {
        guard alive else { return false }
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

final class FakeIcons: MacroIconSource {
    var bytes: [UInt8]?

    init(bytes: [UInt8]? = nil) {
        self.bytes = bytes
    }

    func png(for macro: Macro) -> [UInt8]? {
        bytes
    }
}
