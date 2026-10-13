/// Turns frames from the trusted phone into input and control changes. Every id is checked against the
/// protocol's own enums: an id this Mac does not know is dropped and counted, never guessed at. The phone
/// can name an action; only this table decides what it does.
public final class Dispatcher {
    private static let maxButton: UInt8 = 2
    private static let zoomStep = Int(ScrollAccumulator.wireUnitsPerNotch)
    private static let brightnessStep = 10

    private let input: InputSink
    private let speakers: LevelControl
    private let microphone: LevelControl
    private let brightness: BrightnessControl
    private let display: DisplayRates

    /// Zoom arrives as wheel units; a Mac zooms by key chord, so the units add up until a whole step is owed.
    private var zoomSum = 0

    /// Frames that asked for something unknown, out of range, or unavailable on this Mac.
    public private(set) var dropped = 0

    /// Where the answer to PAD_ATTACH and PAD_DETACH goes. Set by the session once it has a link to write to:
    /// this class deliberately holds no reference to the connection, for the same reason PONG is answered in
    /// the session and not here.
    public var padStatusReply: ((String) -> Void)?

    public init(
        input: InputSink,
        speakers: LevelControl,
        microphone: LevelControl,
        brightness: BrightnessControl,
        display: DisplayRates
    ) {
        self.input = input
        self.speakers = speakers
        self.microphone = microphone
        self.brightness = brightness
        self.display = display
    }

    public func handle(_ frame: Frame) {
        if !carry(frame) {
            dropped += 1
        }
    }

    private func carry(_ frame: Frame) -> Bool {
        switch frame {
        case .move(let dx, let dy):
            input.move(dx: Int(dx), dy: Int(dy))
            return true
        case .scroll(let dx, let dy):
            input.scroll(dx: Int(dx), dy: Int(dy))
            return true
        case .pointerButton(let id, let down):
            guard id <= Dispatcher.maxButton else { return false }
            input.button(id, down: down)
            return true
        case .zoom(let delta):
            zoom(Int(delta))
            return true
        case .runAction(let id):
            return run(id)
        case .setValue(let control, let value):
            guard Int(value) <= Percent.max else { return false }
            return set(control, Int(value))
        case .text(let kind, let value):
            guard kind == TextKind.type.rawValue else { return false }
            input.type(value)
            return true
        case .key(let code, let down):
            return key(code, down: down)
        case .hello, .helloAck, .ping, .pong, .state, .padState:
            // A Mac has no virtual controller to give a pad frame to, and the rest are not the phone's to
            // send once the session is live.
            return false
        }
    }

    /// A Mac has no Ctrl+wheel zoom, so zoom is Command-plus and Command-minus, one press per whole step.
    private func zoom(_ delta: Int) {
        zoomSum += delta
        while zoomSum >= Dispatcher.zoomStep {
            input.chord([.command, .equal])
            zoomSum -= Dispatcher.zoomStep
        }
        while zoomSum <= -Dispatcher.zoomStep {
            input.chord([.command, .minus])
            zoomSum += Dispatcher.zoomStep
        }
    }

    private func key(_ code: UInt16, down: Bool) -> Bool {
        guard let target = KeyMap.target(forWindows: code) else { return false }
        switch target {
        case .key(let key):
            input.key(key, down: down)
        case .media(let media):
            // A media key is a press, not a held key: the release would fire it a second time.
            if down {
                input.media(media)
            }
        }
        return true
    }

    private func run(_ id: UInt8) -> Bool {
        // Macros are a block of ids, not one each, and are not built on the Mac yet.
        let macroStart = Int(ActionId.macroBase.rawValue)
        guard !(macroStart..<(macroStart + ActionId.macroSlots)).contains(Int(id)) else { return false }
        guard let action = ActionId(rawValue: id) else { return false }

        switch action {
        case .muteToggle:
            input.media(.mute)
        case .playPause:
            input.media(.play)
        case .nextTrack:
            input.media(.next)
        case .previousTrack:
            input.media(.previous)
        case .micMuteToggle:
            return microphone.toggleMute()
        case .lock:
            input.chord([.control, .command, .q])
        case .taskView:
            // Mission Control.
            input.chord([.control, .upArrow])
        case .showDesktop:
            input.chord([.f11])
        case .search:
            // Spotlight.
            input.chord([.command, .space])
        case .notifications, .macroBase:
            // Notifications: macOS has no default shortcut for it. The macro block is rejected before this
            // switch; the case is here only so the switch stays exhaustive.
            return false
        case .desktopLeft:
            input.chord([.control, .leftArrow])
        case .desktopRight:
            input.chord([.control, .rightArrow])
        case .appSwitchBegin:
            // Command stays down across frames, exactly as a three-finger swipe keeps the switcher open.
            input.hold(.command)
            input.chord([.tab])
        case .appSwitchNext:
            // Without Command held this would be a bare Tab into whatever has focus.
            guard input.isHeld(.command) else { return false }
            input.chord([.tab])
        case .appSwitchPrevious:
            guard input.isHeld(.command) else { return false }
            input.chord([.shift, .tab])
        case .appSwitchEnd:
            input.release(.command)
        case .zoomReset:
            input.chord([.command, .zero])
            zoomSum = 0
        case .volumeUp:
            input.media(.soundUp)
        case .volumeDown:
            input.media(.soundDown)
        case .brightnessUp:
            return stepBrightness(Dispatcher.brightnessStep, fallback: .brightnessUp)
        case .brightnessDown:
            return stepBrightness(-Dispatcher.brightnessStep, fallback: .brightnessDown)
        case .padAttach, .padDetach:
            // Answered even though a Mac cannot offer a controller: the phone draws a different screen
            // depending on the token, and one that heard nothing could not say why.
            padStatusReply?(PadStatus.noDriver.rawValue)
        }
        return true
    }

    /// Steps the panel from its own level when this Mac will say it; otherwise falls back to the brightness
    /// key, which works on every Mac but cannot be measured.
    private func stepBrightness(_ delta: Int, fallback: MediaKey) -> Bool {
        guard let level = brightness.read() else {
            input.media(fallback)
            return true
        }
        return brightness.set(Percent.clamp(level + delta))
    }

    private func set(_ control: UInt8, _ value: Int) -> Bool {
        guard let id = ControlId(rawValue: control) else { return false }
        switch id {
        case .volume:
            return speakers.setLevel(value)
        case .micLevel:
            return microphone.setLevel(value)
        case .brightness:
            return brightness.set(value)
        case .mediaPosition:
            // Now playing is not built on the Mac yet.
            return false
        case .refreshRate:
            // The value is an index into the rate list, never hertz.
            return value < display.rates.count ? display.select(value) : false
        }
    }
}
