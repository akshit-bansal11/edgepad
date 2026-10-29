import EdgepadCore
import Foundation

/// Every phone's session, however it came: over Bluetooth from `RfcommServer`, or over Wi-Fi or USB from
/// `NetworkServer` once its link has proved itself. One Mac serves one phone, so the phone in use, the connections
/// still in their handshake, the hand-off quiet period and the owner's holds are kept here, once, for both. Everything
/// runs on the main thread, as `Session` requires.
final class SessionHost {
    static let waiting = "Waiting for your phone"
    private static let pollInterval: TimeInterval = 0.5
    /// Outlasts the phone's own twenty seconds of automatic retries, so after a Forget the trust question can only
    /// come back from someone deliberately tapping this Mac on the phone.
    private static let quietAfterForget: TimeInterval = 30
    /// Outlasts the phone's own automatic retries (ten attempts, two seconds apart), so a phone another one just took
    /// over from stops by itself instead of taking this Mac straight back, and two trusted phones cannot displace
    /// each other for ever through their reconnects.
    private static let quietAfterHandOff: TimeInterval = 30

    private let trust: TrustStore
    private let pin: PinStore
    private let askTrust: (String, Cancellation) -> Bool
    private let onStatus: (String) -> Void

    private let speakers = AudioEndpoint(direction: .output)
    private let microphone = AudioEndpoint(direction: .input)
    private let brightness = DisplayBrightness()
    private let display = DisplayModes()
    /// One list for every connection, and for the owner's editor, so a saved change reaches each phone's session.
    let macros = MacroStore.forCurrentUser(launcher: WorkspaceMacroLauncher(), icons: WorkspaceMacroIcons())

    private var pollTimer: Timer?
    private var quietUntil: TimeInterval?
    /// How many owner dialogs (Forget, Security) are open; separate from the quiet period so that ending one cannot
    /// end the other. A count rather than a flag: with two dialogs open, closing the first must not let a phone in
    /// while the second is still on screen, where a connected phone could answer it.
    private var holds = 0
    private var current: Session?
    /// Held for as long as its session is current: an RFCOMM channel only keeps its delegate weakly, and a network
    /// connection is kept by nothing else.
    private var currentTransport: Transport?
    /// Connections still in their handshake, held the same way. None is current until trust has admitted it, so a
    /// phone that merely connects never disturbs the one in use.
    private var candidates: [(session: Session, transport: Transport)] = []
    /// Every phone that lost this Mac to another one within the quiet period, by lowercased address, and the uptime
    /// at which it may connect again. One entry each, so three trusted phones cannot take turns displacing each other.
    private var handedOff: [String: TimeInterval] = [:]
    /// Held while a session is current, so macOS does not throttle the timers and I/O of a windowless app.
    private var activity: NSObjectProtocol?
    /// What the owner reads for a network phone's address, `link:` and 32 hex digits: its name and the way it came.
    /// Filled only by links that proved themselves, so it grows with this Mac's own phones, not with whoever connects.
    private var names: [String: String] = [:]

    /// `pin` is the store the owner's Security panel changes, so the lockout counts and the check that a PIN changed
    /// mid-answer see one another.
    init(
        trust: TrustStore,
        pin: PinStore,
        askTrust: @escaping (String, Cancellation) -> Bool,
        onStatus: @escaping (String) -> Void
    ) {
        self.trust = trust
        self.pin = pin
        self.askTrust = askTrust
        self.onStatus = onStatus
        pollTimer = Timer.scheduledTimer(withTimeInterval: Self.pollInterval, repeats: true) { [weak self] _ in
            self?.current?.poll()
        }
    }

    /// What the owner reads for `address`: a network phone's name and the way it came, a Bluetooth address as it is.
    func shown(_ address: String) -> String {
        names[address] ?? address
    }

    /// True, and logged, when a new connection from `address` is to be turned away before its handshake: a hold or a
    /// quiet period, a phone just taken over from, or one trust would refuse anyway. The phone in use is not touched.
    func refuses(_ address: String) -> Bool {
        if refusing {
            Log.write("Refused \(address): this Mac is forgetting its trusted phones, or just forgot them")
            return true
        }
        if refusesHandedOff(address) {
            return true
        }
        if trust.refuses(address) {
            Log.write(
                "Refused \(address) without disturbing the connected phone: the owner is being asked about a phone "
                    + "or just declined this one, or the trust file cannot be read")
            return true
        }
        return false
    }

    /// Starts the handshake for a phone on `transport`, which delivers its bytes to the session returned. `phone` is
    /// how the owner's trust question names it; `shownAs`, given for a network phone, replaces its address in the
    /// status line. The phone in use is left alone until trust has admitted this one.
    func accept(address: String, phone: String, shownAs: String?, transport: Transport) -> Session {
        if let shownAs {
            names[address] = shownAs
        }
        let injector = InputInjector()
        let dispatcher = Dispatcher(
            input: injector, speakers: speakers, microphone: microphone, brightness: brightness, display: display)
        // Per connection, like the injector: a phone that goes takes its overlay down with it (Session.end).
        dispatcher.annotation = Annotator()
        dispatcher.macros = macros
        // Per connection too: the phone says where captures go each time it connects.
        dispatcher.capture = CaptureToPhone(
            folders: MacCaptureFolders(), lastInput: { [weak dispatcher] in dispatcher?.lastInput ?? 0 })
        let ask = askTrust
        let status = onStatus
        let session = Session(
            address: address,
            transport: transport,
            trust: trust,
            pin: pin,
            askTrust: { _, ended in ask(phone, ended) },
            // Beside the hello timer below, and for each PIN_REQUEST: the session ignores one whose question was
            // already answered.
            schedulePinTimeout: { timedOut in
                _ = Timer.scheduledTimer(withTimeInterval: Session.pinTimeoutSeconds, repeats: false) { _ in
                    timedOut()
                }
            },
            dispatcher: dispatcher,
            input: injector,
            speakers: speakers,
            microphone: microphone,
            brightness: brightness,
            display: display,
            onStatus: { text in status(shownAs.map { text.replacingOccurrences(of: address, with: $0) } ?? text) },
            onAdmitted: { [weak self] admitted in self?.admit(admitted) ?? false },
            onEnded: { [weak self] ended in self?.sessionEnded(ended) })
        candidates.append((session: session, transport: transport))

        Timer.scheduledTimer(withTimeInterval: Session.helloTimeoutSeconds, repeats: false) { [weak session] _ in
            session?.helloTimedOut()
        }
        return session
    }

    /// Ends the phone in use and every connection still in its handshake, and stops polling. On quit.
    func stop() {
        pollTimer?.invalidate()
        pollTimer = nil
        endAll()
        endActivity()
    }

    /// Ends the phone in use and every connection still in its handshake.
    private func endAll() {
        current?.end()
        for candidate in candidates {
            candidate.session.end()
        }
    }

    private func endActivity() {
        if let held = activity {
            ProcessInfo.processInfo.endActivity(held)
            activity = nil
        }
    }

    /// Hangs up and refuses every new connection until each `holdOff()` has had its `resume()`. A phone that is still
    /// trusted reconnects within seconds, so hanging up alone would not keep it out while the owner decides.
    func holdOff() {
        holds += 1
        endAll()
    }

    /// Ends one `holdOff()`; a quiet period started by `stayQuiet()` runs on.
    func resume() {
        holds = max(0, holds - 1)
    }

    /// Whether new connections are being refused, by a hold or a quiet period.
    private var refusing: Bool {
        holds > 0 || quietUntil.map { $0 > ProcessInfo.processInfo.systemUptime } ?? false
    }

    /// Refuses every new connection for a while: a phone just forgotten would otherwise reconnect by itself and
    /// raise the trust question under the cursor of an owner still clicking in the Forget dialog.
    func stayQuiet() {
        // Nothing should be connected after `holdOff()`, and if something is it must not outlive the Forget. Ended
        // first because it posts the waiting status, which the status below must replace.
        endAll()
        // Uptime, not the wall clock: a clock stepped backwards must not stretch the quiet period. Never shortens
        // one already running.
        quietUntil = max(quietUntil ?? 0, ProcessInfo.processInfo.systemUptime + Self.quietAfterForget)
        onStatus("Forgot the trusted phones; ready for a new one in \(Int(Self.quietAfterForget)) seconds")
        Timer.scheduledTimer(withTimeInterval: Self.quietAfterForget, repeats: false) { [weak self] _ in
            guard let self, self.current == nil else { return }
            self.onStatus(Self.waiting)
        }
    }

    /// True, and logged, when `address` is a phone another one took over from within the quiet period. Expired
    /// entries are dropped first, so the record never outgrows the phones displaced in the last half minute.
    private func refusesHandedOff(_ address: String) -> Bool {
        let now = ProcessInfo.processInfo.systemUptime
        handedOff = handedOff.filter { $0.value > now }
        guard handedOff[address.lowercased()] != nil else { return false }
        Log.write(
            "Refused \(address): another phone took over this Mac less than \(Int(Self.quietAfterHandOff)) seconds "
                + "ago")
        return true
    }

    /// Trust has said yes to `candidate`, which now takes over from the phone in use, if any. False when this Mac
    /// started refusing connections while the owner was being asked, or when the candidate is a phone just taken
    /// over from: a connection it opened just before the hand-off must not take this Mac straight back.
    private func admit(_ candidate: Session) -> Bool {
        guard !refusing, !refusesHandedOff(candidate.address) else { return false }
        guard let index = candidates.firstIndex(where: { $0.session === candidate }) else { return false }
        let transport = candidates.remove(at: index).transport
        let previous = current
        current = candidate
        currentTransport = transport
        // The same address is the phone replacing its own link: after a drop it reconnects before the old channel
        // has noticed it is dead, and there is no other phone to keep out.
        if let previous, previous.address.caseInsensitiveCompare(candidate.address) != .orderedSame {
            handedOff[previous.address.lowercased()] = ProcessInfo.processInfo.systemUptime + Self.quietAfterHandOff
        }
        // AllowingIdleSystemSleep on purpose, so a connected phone does not keep the Mac awake.
        if activity == nil {
            activity = ProcessInfo.processInfo.beginActivity(
                options: [.userInitiatedAllowingIdleSystemSleep, .latencyCritical],
                reason: "A phone is controlling this Mac")
        }
        previous?.end()
        return true
    }

    /// The phone in use ending leaves this Mac waiting. A candidate ending puts back the status of the phone still
    /// in use, which the candidate's question or refusal replaced.
    private func sessionEnded(_ ended: Session) {
        if current === ended {
            current = nil
            endActivity()
            currentTransport = nil
            onStatus(Self.waiting)
            return
        }
        guard let index = candidates.firstIndex(where: { $0.session === ended }) else { return }
        candidates.remove(at: index)
        onStatus(current.map { "Connected to \(shown($0.address))" } ?? Self.waiting)
    }
}
