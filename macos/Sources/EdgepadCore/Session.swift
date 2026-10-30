import Foundation

/// One connected phone: handshake, trust check (asking the owner, through `askTrust`, about a phone not trusted
/// yet), the PIN at security levels 2 and 3, the server's word through `onAdmitted`, then each frame straight into
/// the dispatcher. The Mac's own state (volume, mute, brightness) goes back as STATE frames: a snapshot after the
/// handshake, then every change `poll` finds, so the phone's dials show what the Mac is really at.
///
/// Transport-agnostic and single-threaded: the app calls every method on the main thread.
public final class Session {
    private static let mutedFlag: UInt8 = 1
    /// How long a connection may stay silent before the app's timer calls `helloTimedOut`. The timer and the
    /// log line read this one number, so they cannot disagree.
    public static let helloTimeoutSeconds: TimeInterval = 10
    /// How long a phone has to answer each PIN_REQUEST: the same minute the owner gets for the trust question.
    public static let pinTimeoutSeconds: TimeInterval = 60
    /// How often the macro apps are surveyed while the phone's grid is open.
    private static let macroSurveySeconds: TimeInterval = 1

    private enum Stage {
        case hello
        /// PIN_REQUEST sent; only PIN_ANSWER is legal until HELLO_ACK.
        case awaitingPin
        case live
    }

    public let address: String
    /// True once HELLO_ACK was sent.
    public var isLive: Bool { stage == .live }
    public private(set) var isEnded = false

    private var stage = Stage.hello
    /// Counts the PIN_REQUESTs sent, so a timer armed for an earlier question cannot end the wait for a later one.
    private var questions = 0

    private let transport: Transport
    private let trust: TrustStore
    private let pin: PinStore
    /// Handed the session's end, so a question still open when a hold or a hang-up ends the session is withdrawn.
    private let askTrust: (String, Cancellation) -> Bool
    /// Arms the app's timer for one PIN_REQUEST: the closure it is handed ends the session if that question is still
    /// unanswered when called. Session keeps no timers of its own, so a test can fire the timeout by hand.
    private let schedulePinTimeout: (@escaping () -> Void) -> Void
    private let dispatcher: Dispatcher
    private let input: InputSink
    private let speakers: LevelControl
    private let microphone: LevelControl
    private let brightness: BrightnessControl
    private let display: DisplayRates
    private let identity: LinkIdentity
    private let onStatus: (String) -> Void
    /// Called once trust has said yes, just before HELLO_ACK: the server decides there whether this phone may take
    /// over the Mac from the one in use. False ends the session with nothing sent.
    private let onAdmitted: (Session) -> Bool
    private let onEnded: (Session) -> Void
    /// Runs one macro icon's turn. The main queue by default, so frames that arrive meanwhile are read between
    /// icons rather than after all of them. Tests pass `{ $0() }` so the icons follow `receive` directly.
    private let schedule: (@escaping () -> Void) -> Void
    /// Runs the macro survey's next tick after a delay, once. Tests hold the ticks and fire them by hand.
    private let scheduleAfter: (TimeInterval, @escaping () -> Void) -> Void
    /// Which icon run may still send: each WANT_ICONS moves it on, and a run that sees it moved stops.
    private var iconRun = 0
    /// Which macro watch may still survey: MACROS_WATCH and MACROS_UNWATCH move it on, and a tick that sees it moved
    /// stops.
    private var macroSurveyRun = 0
    /// The states the phone was last told, so it hears only a change.
    private var lastMacroOpen: String?

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
    /// The label list's subscription, kept so `end()` can let go of it and a dead session is not written to.
    private var macroWatch: MacroWatch?
    /// Cancelled by `end()`, so the owner's question about this phone is never put, or is withdrawn if it is open.
    private let ended = Cancellation()

    public init(
        address: String,
        transport: Transport,
        trust: TrustStore,
        pin: PinStore,
        askTrust: @escaping (String, Cancellation) -> Bool,
        schedulePinTimeout: @escaping (@escaping () -> Void) -> Void,
        dispatcher: Dispatcher,
        input: InputSink,
        speakers: LevelControl,
        microphone: LevelControl,
        brightness: BrightnessControl,
        display: DisplayRates,
        identity: LinkIdentity,
        onStatus: @escaping (String) -> Void,
        onAdmitted: @escaping (Session) -> Bool,
        onEnded: @escaping (Session) -> Void,
        schedule: @escaping (@escaping () -> Void) -> Void = { DispatchQueue.main.async(execute: $0) },
        scheduleAfter: @escaping (TimeInterval, @escaping () -> Void) -> Void = { seconds, work in
            DispatchQueue.main.asyncAfter(deadline: .now() + seconds, execute: work)
        }
    ) {
        self.address = address
        self.transport = transport
        self.trust = trust
        self.pin = pin
        self.askTrust = askTrust
        self.schedulePinTimeout = schedulePinTimeout
        self.dispatcher = dispatcher
        self.input = input
        self.speakers = speakers
        self.microphone = microphone
        self.brightness = brightness
        self.display = display
        self.identity = identity
        self.onStatus = onStatus
        self.onAdmitted = onAdmitted
        self.onEnded = onEnded
        self.schedule = schedule
        self.scheduleAfter = scheduleAfter
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
    /// A phone that has reached the PIN question is not ended here: its own timeout covers it.
    public func helloTimedOut() {
        guard stage == .hello, !isEnded, !isAsking else { return }
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
        ended.cancel()
        input.releaseAll()
        dispatcher.padStatusReply = nil
        dispatcher.macroWatch = nil
        macroWatch?.cancel()
        macroWatch = nil
        // The overlay goes with the session: a phone that vanishes mid-drawing must leave no layer on the screen.
        dispatcher.annotation?.end()
        dispatcher.capture?.end()
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
            switch stage {
            case .hello:
                greet(frame)
            case .awaitingPin:
                answerPin(frame)
            case .live:
                handleLive(frame)
            }
        }

        // After the rest of this read, so nothing is handled out of arrival order. Bytes held while the owner was
        // asked reach the PIN question too, where anything but PIN_ANSWER ends the session.
        if stage != .hello, !isEnded, !pending.isEmpty {
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

        // A phone locked out by wrong PINs is told so and closed before the owner is asked anything, so
        // guessing cannot also be a way to keep the trust question on the owner's screen.
        if lockedOut() {
            onStatus("Refused a phone locked out after wrong PINs")
            end()
            return
        }

        // HELLO_ACK waits on the owner's answer, and a refusal closes the link without one: the same
        // bytes a phone it did not trust has always been sent, so no phone needs to learn anything new.
        //
        // A session ended before the question (a hold for the owner's Security panel, a hang-up) is never asked
        // about, and one ended while it is open has its answer ignored, a yes included. Both throw rather than
        // answer no, so the owner is not recorded as having declined a phone they never answered about.
        isAsking = true
        let admitted: Bool
        do {
            admitted = try trust.admit(address) {
                try ended.check()
                let yes = askTrust(address, ended)
                try ended.check()
                return yes
            }
        } catch {
            isAsking = false
            Log.write("Refused \(address): its session ended before the owner answered")
            end()
            return
        }
        isAsking = false
        guard !isEnded else { return }
        guard admitted else {
            let trusted = trust.trustedPhones.joined(separator: ", ")
            Log.write("Refused \(address): not trusted; this Mac trusts \(trusted.isEmpty ? "no phone yet" : trusted)")
            onStatus("Refused a phone it does not trust")
            end()
            return
        }

        // The PIN at levels 2 and 3, and still before onAdmitted: a phone that has not given it must never be able
        // to end the connected phone's session. Level 1 sends nothing here, as before there was a PIN.
        guard pin.required(address) else {
            admit()
            return
        }
        // Checked again here, not only before the trust question: another connection from the same address may
        // have used up the tries while the owner was answering.
        if lockedOut() {
            onStatus("Refused a phone that did not give the right PIN")
            end()
            return
        }
        askForPin(attemptsLeft: pin.attemptsLeft(address))
    }

    /// The phone's answer to PIN_REQUEST. The check runs PBKDF2 on the main thread, hundreds of milliseconds at the
    /// real work factor; it happens once per question, and the session has nothing else to do while it waits.
    private func answerPin(_ frame: Frame) {
        guard case .pinAnswer(let answer) = frame else {
            // A protocol error, like a bad flag byte: the phone was asked one question and answered another.
            Log.write("Session with \(address) ended: it sent another frame instead of the PIN")
            end()
            return
        }

        switch pin.check(address, answer) {
        case .accepted:
            Log.write("PIN accepted for \(address)")
            admit()
        case .wrong(let attemptsLeft):
            Log.write("PIN rejected for \(address)")
            askForPin(attemptsLeft: attemptsLeft)
        case .locked(let seconds):
            send(.pinRequest(attemptsLeft: 0, lockedSeconds: UInt16(clamping: seconds)))
            Log.write("PIN rejected for \(address): locked out for \(seconds) s")
            onStatus("Refused a phone that did not give the right PIN")
            end()
        case .unavailable:
            // Unreadable settings, no PIN, or the PIN or level changed while this answer was being hashed. Not the
            // phone's fault, so not counted against it; but nothing proved it may come in either, and it is closed
            // without HELLO_ACK to connect again under whatever is now in force.
            Log.write("Refused \(address): its PIN could not be checked against the current security settings")
            onStatus("Refused a phone that did not give the right PIN")
            end()
        }
    }

    /// Sends PIN_REQUEST and arms its timeout. Armed for each question, so a phone gets a full minute for every try
    /// rather than a minute for all five.
    private func askForPin(attemptsLeft: Int) {
        send(.pinRequest(attemptsLeft: UInt8(clamping: attemptsLeft), lockedSeconds: 0))
        guard !isEnded else { return }
        stage = .awaitingPin
        questions += 1
        let question = questions
        schedulePinTimeout { [weak self] in self?.pinTimedOut(question) }
    }

    private func pinTimedOut(_ question: Int) {
        guard stage == .awaitingPin, question == questions, !isEnded else { return }
        Log.write("Refused \(address): no PIN within \(Int(Session.pinTimeoutSeconds)) s")
        end()
    }

    /// Sends the lock-out reply and returns true when this phone is locked out. Only where a PIN is required, so
    /// level 1 never sends a PIN frame, even with failures still counted from before the level was lowered.
    private func lockedOut() -> Bool {
        guard pin.required(address), let wait = pin.lockedFor(address) else { return false }
        // Rounded up and kept in the frame's u16, so a phone told to wait is never let back early.
        let seconds = UInt16(clamping: max(1, Int(wait.rounded(.up))))
        send(.pinRequest(attemptsLeft: 0, lockedSeconds: seconds))
        Log.write("Refused \(address): locked out after wrong PINs for \(seconds) s more")
        return true
    }

    /// Hands the phone to the server and, if it is taken, sends HELLO_ACK and goes live.
    private func admit() {
        guard onAdmitted(self) else {
            Log.write("Refused \(address): trusted, but not taken now: phones are being forgotten, or it lost the Mac")
            end()
            return
        }

        send(.helloAck(version: ProtocolConstants.version))
        guard !isEnded else { return }
        stage = .live
        Log.write("Connected \(address)")
        onStatus("Connected to \(address)")
        reportState()
    }

    private func handleLive(_ frame: Frame) {
        switch frame {
        case .ping(let time):
            send(.pong(time: time))
        case .text(let kind, let value) where kind == TextKind.wantIcons.rawValue:
            // Not a frame the dispatcher should count as dropped, whether or not there is anything to send.
            startIcons(size: MacroIconChunks.snap(value, max: MacroIconChunks.largestSize))
        default:
            dispatcher.handle(frame)
        }
    }

    /// What the phone's dials start from, in the order docs/PROTOCOL.md describes.
    private func reportState() {
        send(.text(kind: TextKind.device.rawValue, value: identity.deviceText))
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
        // A capture on its way writes through the session, so a dead link ends the session here as for any send.
        dispatcher.capture?.send = { [weak self] frame in
            guard let self, !self.isEnded else { return false }
            self.send(frame)
            return !self.isEnded
        }
        dispatcher.padStatusReply = { [weak self] status in
            self?.send(.text(kind: TextKind.padStatus.rawValue, value: status))
        }
        send(.text(kind: TextKind.padStatus.rawValue, value: PadStatus.noDriver.rawValue))

        // Only what this build can do: the phone hides a feature that is not named, so naming one that is not
        // here would be a button that quietly does nothing.
        var features = [Feature.capture.rawValue]
        if let annotation = dispatcher.annotation {
            features.insert(Feature.annotate.rawValue, at: 0)
            // Written like the pad's status, for the same reason: a dead link here should end the session.
            annotation.signStatusReply = { [weak self] status in
                self?.send(.text(kind: TextKind.signStatus.rawValue, value: status))
            }
            annotation.screenSizeChanged = { [weak self] in self?.sendScreenSize() }
        }
        if dispatcher.macros?.seesApps == true {
            features.append(Feature.running.rawValue)
            dispatcher.macroWatch = { [weak self] open in self?.watchMacroApps(open) }
        }
        send(.text(kind: TextKind.features.rawValue, value: features.joined(separator: "/")))
        sendScreenSize()

        // Watched rather than sent once, so a macro the owner adds while the phone is connected reaches it. With
        // no store the list is empty, and the phone then says to add some on the Mac instead of drawing a broken
        // grid.
        if let macros = dispatcher.macros {
            macroWatch = macros.watch { [weak self] names in
                self?.send(.text(kind: TextKind.macros.rawValue, value: names))
            }
        } else {
            send(.text(kind: TextKind.macros.rawValue, value: ""))
        }
    }

    /// The phone's macro grid opened or closed. Each opening hears the state afresh, whatever was last sent.
    private func watchMacroApps(_ open: Bool) {
        macroSurveyRun += 1
        lastMacroOpen = nil
        if open {
            surveyMacroApps(run: macroSurveyRun)
        }
    }

    /// One survey, one send if it changed anything, and the next tick. `end()` stops it through `isEnded`.
    private func surveyMacroApps(run: Int) {
        guard !isEnded, run == macroSurveyRun, let macros = dispatcher.macros else { return }
        let states = macros.openStates()
        if states != lastMacroOpen {
            lastMacroOpen = states
            send(.text(kind: TextKind.macroOpen.rawValue, value: states))
        }
        scheduleAfter(Session.macroSurveySeconds) { [weak self] in self?.surveyMacroApps(run: run) }
    }

    /// Every macro icon the app can supply, one icon per scheduled turn and none inline: the phone asks as soon as
    /// it connects and whenever the list changes, so a macro tapped then would otherwise wait behind every icon. A
    /// newer WANT_ICONS supersedes this run, and `end()` stops it.
    private func startIcons(size: Int) {
        guard let macros = dispatcher.macros else { return }
        iconRun += 1
        sendIcons(from: 0, of: macros, revision: macros.revision, run: iconRun, size: size)
    }

    /// The icon in `slot`, in pieces, then the next slot's turn. An icon that cannot be had is simply not sent and
    /// its button keeps its label.
    private func sendIcons(from slot: Int, of macros: MacroStore, revision: Int, run: Int, size: Int) {
        schedule { [weak self] in
            guard let self, !self.isEnded, self.iconRun == run, macros.revision == revision,
                slot < macros.macros.count
            else { return }
            if let png = macros.icon(at: slot, size: size) {
                for chunk in MacroIconChunks.chunks(slot: slot, png: png) {
                    // A save swaps the list between chunks of the old one, which would land on whatever moved into
                    // the slot. The phone asks again for the new list.
                    guard macros.revision == revision else { return }
                    self.send(.text(kind: TextKind.macroIcon.rawValue, value: chunk))
                }
            }
            self.sendIcons(from: slot + 1, of: macros, revision: revision, run: run, size: size)
        }
    }

    /// The phone maps its pad onto this aspect ratio. Nothing is sent without an overlay, so an older phone and a
    /// Mac with no screen both leave the Annotate tile off.
    private func sendScreenSize() {
        guard let size = dispatcher.annotation?.screenSize, size.width > 0, size.height > 0 else { return }
        send(.text(kind: TextKind.screenSize.rawValue, value: "\(size.width)/\(size.height)"))
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
