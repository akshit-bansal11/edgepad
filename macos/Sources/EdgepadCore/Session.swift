import Foundation

/// One connected phone: handshake, trust check (asking the owner, through `askTrust`, about a phone not trusted
/// yet), the server's word through `onAdmitted`, then each frame straight into the dispatcher. The Mac's own state
/// (volume, mute, brightness) goes back as STATE frames: a snapshot after the handshake, then every change `poll`
/// finds, so the phone's dials show what the Mac is really at.
///
/// Transport-agnostic and single-threaded: the app calls every method on the main thread.
public final class Session {
    private static let mutedFlag: UInt8 = 1
    /// How long a connection may stay silent before the app's timer calls `helloTimedOut`. The timer and the
    /// log line read this one number, so they cannot disagree.
    public static let helloTimeoutSeconds: TimeInterval = 10

    public let address: String
    /// True once HELLO_ACK was sent.
    public private(set) var isLive = false
    public private(set) var isEnded = false

    private let transport: Transport
    private let trust: TrustStore
    private let askTrust: (String) -> Bool
    private let dispatcher: Dispatcher
    private let input: InputSink
    private let speakers: LevelControl
    private let microphone: LevelControl
    private let brightness: BrightnessControl
    private let display: DisplayRates
    private let onStatus: (String) -> Void
    /// Called once trust has said yes, just before HELLO_ACK: the server decides there whether this phone may take
    /// over the Mac from the one in use. False ends the session with nothing sent.
    private let onAdmitted: (Session) -> Bool
    private let onEnded: (Session) -> Void

    private var reader = FrameReader()

    /// The owner's question is a modal one that can take a minute, during which more bytes can arrive and
    /// `receive` can be entered again. They wait here, so they are read after HELLO_ACK and not before it.
    private var isAsking = false
    private var pending: [UInt8] = []
    /// A healthy phone sends almost nothing before HELLO_ACK, so this is generous. Without it an untrusted
    /// paired device could stream megabytes during the question and have them all handled in one run.
    private static let maxPendingBytes = 64 * 1024

    private var lastVolume: Level?
    private var lastMicrophone: Level?
    private var lastBrightness: Int?

    public init(
        address: String,
        transport: Transport,
        trust: TrustStore,
        askTrust: @escaping (String) -> Bool,
        dispatcher: Dispatcher,
        input: InputSink,
        speakers: LevelControl,
        microphone: LevelControl,
        brightness: BrightnessControl,
        display: DisplayRates,
        onStatus: @escaping (String) -> Void,
        onAdmitted: @escaping (Session) -> Bool,
        onEnded: @escaping (Session) -> Void
    ) {
        self.address = address
        self.transport = transport
        self.trust = trust
        self.askTrust = askTrust
        self.dispatcher = dispatcher
        self.input = input
        self.speakers = speakers
        self.microphone = microphone
        self.brightness = brightness
        self.display = display
        self.onStatus = onStatus
        self.onAdmitted = onAdmitted
        self.onEnded = onEnded
    }

    /// Bytes from the phone, in arrival order.
    public func receive(_ bytes: [UInt8]) {
        guard !isEnded else { return }
        if isAsking {
            guard pending.count + bytes.count <= Session.maxPendingBytes else {
                Log.write("Session with \(address) ended: it sent too much while the owner was being asked")
                end()
                return
            }
            pending.append(contentsOf: bytes)
            return
        }
        process(bytes)
    }

    /// The app calls this once the connection has been open for ten seconds. A paired device that connects and
    /// says nothing would otherwise hold the one session this Mac serves for as long as the link stayed up.
    public func helloTimedOut() {
        guard !isLive, !isEnded, !isAsking else { return }
        Log.write("Refused \(address): no HELLO within \(Int(Session.helloTimeoutSeconds)) s")
        end()
    }

    /// The app calls this twice a second. A change made on the Mac itself reaches the phone's dials here.
    public func poll() {
        guard isLive, !isEnded else { return }
        if let level = speakers.read(), level != lastVolume {
            lastVolume = level
            sendLevel(.volume, level)
        }
        if let level = microphone.read(), level != lastMicrophone {
            lastMicrophone = level
            sendLevel(.micLevel, level)
        }
        if let percent = brightness.read(), percent != lastBrightness {
            lastBrightness = percent
            sendBrightness(percent)
        }
    }

    /// Idempotent. Anything still held down is lifted first: a link dropped mid-gesture must never leave
    /// Command or a mouse button stuck with nothing left alive to release it.
    public func end() {
        guard !isEnded else { return }
        isEnded = true
        input.releaseAll()
        dispatcher.padStatusReply = nil
        if dispatcher.dropped > 0 {
            Log.write("Session with \(address): \(dispatcher.dropped) frames dropped")
        }
        transport.close()
        onEnded(self)
    }

    private func process(_ bytes: [UInt8]) {
        let frames: [Frame]
        do {
            frames = try reader.append(bytes)
        } catch {
            Log.write("Session with \(address) ended: \(error)")
            end()
            return
        }

        for frame in frames {
            guard !isEnded else { return }
            if isLive {
                handleLive(frame)
            } else {
                greet(frame)
            }
        }

        // After the rest of this read, so nothing is handled out of arrival order.
        if isLive, !isEnded, !pending.isEmpty {
            let waiting = pending
            pending = []
            process(waiting)
        }
    }

    private func greet(_ frame: Frame) {
        guard case .hello(let version) = frame else {
            Log.write("Refused \(address): no valid HELLO")
            end()
            return
        }

        guard version == ProtocolConstants.version else {
            // Answered with this Mac's version, so the phone can say which side needs updating.
            send(.helloAck(version: ProtocolConstants.version))
            Log.write("Refused \(address): it speaks protocol \(version), this Mac \(ProtocolConstants.version)")
            onStatus("Refused a phone on protocol \(version); this Mac is on \(ProtocolConstants.version)")
            end()
            return
        }

        // HELLO_ACK waits on the owner's answer, and a refusal closes the link without one: the same
        // bytes a phone it did not trust has always been sent, so no phone needs to learn anything new.
        isAsking = true
        let admitted = trust.admit(address) { askTrust(address) }
        isAsking = false
        guard !isEnded else { return }
        guard admitted else {
            let trusted = trust.trustedPhones.joined(separator: ", ")
            Log.write("Refused \(address): not trusted; this Mac trusts \(trusted.isEmpty ? "no phone yet" : trusted)")
            onStatus("Refused a phone it does not trust")
            end()
            return
        }
        guard onAdmitted(self) else {
            Log.write("Refused \(address): this Mac stopped taking connections while it asked")
            end()
            return
        }

        send(.helloAck(version: ProtocolConstants.version))
        guard !isEnded else { return }
        isLive = true
        Log.write("Connected \(address)")
        onStatus("Connected to \(address)")
        reportState()
    }

    private func handleLive(_ frame: Frame) {
        switch frame {
        case .ping(let time):
            send(.pong(time: time))
        case .text(let kind, _) where kind == TextKind.wantIcons.rawValue:
            // No macros, so no icons; not a frame the dispatcher should count as dropped.
            break
        default:
            dispatcher.handle(frame)
        }
    }

    /// What the phone's dials start from, in the order docs/PROTOCOL.md describes.
    private func reportState() {
        if let level = speakers.read() {
            lastVolume = level
            sendLevel(.volume, level)
        }
        if let level = microphone.read() {
            lastMicrophone = level
            sendLevel(.micLevel, level)
        }
        if let percent = brightness.read() {
            lastBrightness = percent
            sendBrightness(percent)
        }

        // The rate list goes first because the level is an index into it. A display that offers nothing
        // sends an empty list, and the phone's dial then has no travel.
        display.refresh()
        let rates = display.rates.map { String($0) }.joined(separator: "/")
        send(.text(kind: TextKind.refreshRates.rawValue, value: rates))
        if let current = display.current {
            send(.state(control: ControlId.refreshRate.rawValue, value: UInt8(clamping: current), flags: 0))
        }

        // Written like a PONG and not like a watcher's report: a dead link here should end the session. The
        // opening answer is sent before the phone can ask, so it can hide the pad rather than offer a button
        // that quietly does nothing.
        dispatcher.padStatusReply = { [weak self] status in
            self?.send(.text(kind: TextKind.padStatus.rawValue, value: status))
        }
        send(.text(kind: TextKind.padStatus.rawValue, value: PadStatus.noDriver.rawValue))

        // An empty list: the phone then says to add some on the laptop instead of drawing a broken grid.
        send(.text(kind: TextKind.macros.rawValue, value: ""))
    }

    private func sendLevel(_ control: ControlId, _ level: Level) {
        let flags = level.muted ? Session.mutedFlag : 0
        send(.state(control: control.rawValue, value: percentByte(level.percent), flags: flags))
    }

    private func sendBrightness(_ percent: Int) {
        send(.state(control: ControlId.brightness.rawValue, value: percentByte(percent), flags: 0))
    }

    private func percentByte(_ percent: Int) -> UInt8 {
        UInt8(clamping: Percent.clamp(percent))
    }

    /// A send that finds the link dead ends the session, so nothing after it writes to a closed link.
    private func send(_ frame: Frame) {
        guard !isEnded else { return }
        if !transport.send(FrameCodec.encode(frame)) {
            Log.write("Session with \(address) ended: the link stopped taking data")
            end()
        }
    }
}
