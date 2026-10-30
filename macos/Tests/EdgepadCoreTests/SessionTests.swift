import CryptoKit
import Foundation
import XCTest

@testable import EdgepadCore

private final class SessionBox {
    var session: Session?
}

final class SessionTests: XCTestCase {
    private let phone = "AA:BB:CC:DD:EE:FF"
    private let input = FakeInput()
    private let transport = FakeTransport()
    private let speakers = FakeLevel(level: Level(percent: 40, muted: true))
    private let microphone = FakeLevel(level: Level(percent: 30, muted: false))
    private let brightness = FakeBrightness(level: 70)
    private let display = FakeDisplay(rates: [60, 120], current: 1)
    private let directory = FileManager.default.temporaryDirectory
        .appendingPathComponent("edgepad-session-\(UUID().uuidString)")

    private var statuses: [String] = []
    private var ended: [Session] = []
    /// What the server answers when trust admits a phone, and how many frames had been sent each time it was asked.
    private var admits = true
    private var admissions: [Int] = []
    /// The PIN timeouts the session asked for, one per PIN_REQUEST, fired by hand.
    private var timeouts: [() -> Void] = []
    /// The session's end, as the owner's question was handed it.
    private var askedWith: Cancellation?
    /// Seconds on the PIN store's clock, moved by hand.
    private var time: TimeInterval = 0
    /// False runs each icon turn as soon as it is scheduled; true holds them here until `runIconTurns`.
    private var holdsIcons = false
    private var iconTurns: [() -> Void] = []
    /// The macro survey's next ticks, as the session armed them, fired by hand.
    private var watchTicks: [(seconds: TimeInterval, tick: () -> Void)] = []
    /// A blob's next tick, as its sender armed it, fired by hand.
    private var blobTicks: [() -> Void] = []
    /// Keeps a session alive as `SessionHost` does: its writers hold it weakly, so a test that drops it has no link.
    private var heldSession: Session?

    private lazy var trust = TrustStore(path: directory.appendingPathComponent("trust").path)
    /// One iteration, as in PinStoreTests: the real work factor would take most of a second per answer.
    private lazy var pins = PinStore(
        directory: directory.path, now: { [weak self] in self?.time ?? 0 }, iterations: 1)
    private lazy var dispatcher = Dispatcher(
        input: input,
        speakers: speakers,
        microphone: microphone,
        brightness: brightness,
        display: display
    )

    override func setUpWithError() throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try FileManager.default.removeItem(at: directory)
    }

    private func makeSession(
        address: String? = nil,
        ask: @escaping (String) -> Bool = { _ in true }
    ) -> Session {
        Session(
            address: address ?? phone,
            transport: transport,
            trust: trust,
            pin: pins,
            askTrust: { [weak self] address, ended in
                self?.askedWith = ended
                return ask(address)
            },
            schedulePinTimeout: { [weak self] timedOut in self?.timeouts.append(timedOut) },
            dispatcher: dispatcher,
            input: input,
            speakers: speakers,
            microphone: microphone,
            brightness: brightness,
            display: display,
            identity: LinkIdentity(id: Array(UInt8(16)..<32), name: "Mac"),
            onStatus: { [weak self] status in self?.statuses.append(status) },
            onAdmitted: { [weak self] _ in self?.admitted() ?? false },
            onEnded: { [weak self] session in self?.ended.append(session) },
            schedule: { [weak self] turn in
                guard let self, self.holdsIcons else { return turn() }
                self.iconTurns.append(turn)
            },
            scheduleAfter: { [weak self] seconds, tick in self?.watchTicks.append((seconds, tick)) }
        )
    }

    /// Runs each held icon turn in order, including the turns those queue.
    private func runIconTurns() {
        while !iconTurns.isEmpty {
            iconTurns.removeFirst()()
        }
    }

    private func admitted() -> Bool {
        admissions.append(transport.sent.count)
        return admits
    }

    private func connectedSession() -> Session {
        let session = makeSession()
        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
        return session
    }

    /// Only ever handed six digits written into the test, so the unwrap cannot fail.
    private func answer(_ text: String) -> [UInt8] {
        FrameCodec.encode(.pinAnswer(Pin(digits: text.compactMap { $0.wholeNumberValue }.map { UInt8($0) })!))
    }

    private func setLevel(_ level: Int) {
        XCTAssertTrue(pins.setPin(Pin(digits: [1, 2, 3, 4, 5, 6])!))
        XCTAssertTrue(pins.setLevel(level))
    }

    /// The frames sent since `count` had been.
    private func framesSince(_ count: Int) -> [Frame] {
        Array(transport.frames().dropFirst(count))
    }

    private func question(_ attemptsLeft: UInt8, locked: UInt16 = 0) -> Frame {
        .pinRequest(attemptsLeft: attemptsLeft, lockedSeconds: locked)
    }

    private func logSize() -> Int {
        (try? Data(contentsOf: URL(fileURLWithPath: Log.filePath)))?.count ?? 0
    }

    /// What the log gained after it was `offset` bytes long; all of it if it was rotated meanwhile.
    private func logSince(_ offset: Int) -> String {
        let data = (try? Data(contentsOf: URL(fileURLWithPath: Log.filePath))) ?? Data()
        return String(decoding: data.count >= offset ? data.dropFirst(offset) : data, as: UTF8.self)
    }

    func testAFirstFrameThatIsNotHelloEndsTheSessionWithNothingSent() {
        let session = makeSession()

        session.receive(FrameCodec.encode(.ping(time: 1)))

        XCTAssertTrue(session.isEnded)
        XCTAssertEqual(transport.sent.count, 0)
    }

    func testAWrongVersionIsAnsweredWithThisSidesVersionAndEnded() {
        let session = makeSession()

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version + 1)))

        XCTAssertEqual(transport.frames(), [.helloAck(version: ProtocolConstants.version)])
        XCTAssertTrue(session.isEnded)
        XCTAssertFalse(session.isLive)
    }

    func testAnUntrustedPhoneTheOwnerRefusesGetsNoHelloAck() {
        let session = makeSession(ask: { _ in false })

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        XCTAssertEqual(transport.sent.count, 0)
        XCTAssertTrue(session.isEnded)
        XCTAssertEqual(statuses, ["Refused a phone it does not trust"])
    }

    func testAnAcceptedPhoneIsGivenHelloAckThenTheStateReport() {
        let session = connectedSession()

        XCTAssertTrue(session.isLive)
        XCTAssertEqual(
            transport.frames(),
            [
                .helloAck(version: ProtocolConstants.version),
                .text(kind: TextKind.device.rawValue, value: "101112131415161718191a1b1c1d1e1f/macos/Mac"),
                .state(control: ControlId.volume.rawValue, value: 40, flags: 1),
                .state(control: ControlId.micLevel.rawValue, value: 30, flags: 0),
                .state(control: ControlId.brightness.rawValue, value: 70, flags: 0),
                .text(kind: TextKind.refreshRates.rawValue, value: "60/120"),
                .state(control: ControlId.refreshRate.rawValue, value: 1, flags: 0),
                .text(kind: TextKind.padStatus.rawValue, value: "no-driver"),
                .text(kind: TextKind.features.rawValue, value: Feature.capture.rawValue),
                .text(kind: TextKind.macros.rawValue, value: ""),
            ]
        )
    }

    func testAnOverlayIsOfferedAsAFeatureWithTheScreenSizeAndTakenDownWithTheSession() {
        let annotation = FakeAnnotation()
        dispatcher.annotation = annotation

        let session = connectedSession()
        let frames = transport.frames()

        XCTAssertTrue(frames.contains(.text(kind: TextKind.features.rawValue, value: "annotate/capture")))
        XCTAssertTrue(frames.contains(.text(kind: TextKind.screenSize.rawValue, value: "1920/1080")))
        annotation.signStatusReply?(SignStatus.saved.rawValue)
        XCTAssertEqual(transport.frames().last, .text(kind: TextKind.signStatus.rawValue, value: "saved"))

        session.end()
        XCTAssertEqual(annotation.endCount, 1)
    }

    func testAnAcceptedPhoneIsReportedAsConnected() {
        _ = connectedSession()

        XCTAssertEqual(statuses, ["Connected to \(phone)"])
    }

    func testAPhoneOtherThanTheTrustedOneIsAsked() {
        var asked = false
        _ = trust.admit("11:22:33:44:55:66") { true }
        let session = makeSession(ask: { _ in
            asked = true
            return true
        })

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        XCTAssertTrue(asked)
        XCTAssertTrue(session.isLive)
    }

    func testAnAdmittedPhoneIsHandedToTheServerOnceBeforeHelloAck() {
        let session = connectedSession()

        XCTAssertEqual(admissions, [0])
        XCTAssertEqual(transport.frames().first, .helloAck(version: ProtocolConstants.version))
        XCTAssertTrue(session.isLive)
    }

    func testARefusedPhoneIsNeverHandedToTheServer() {
        let session = makeSession(ask: { _ in false })

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        XCTAssertEqual(admissions, [])
        XCTAssertTrue(session.isEnded)
    }

    func testAPhoneTheServerWillNotTakeEndsWithNoHelloAck() {
        admits = false
        let session = makeSession()

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        XCTAssertEqual(admissions, [0])
        XCTAssertEqual(transport.sent.count, 0)
        XCTAssertTrue(session.isEnded)
        XCTAssertFalse(session.isLive)
        XCTAssertEqual(ended.count, 1)
    }

    func testPingIsAnsweredWithTheSameTime() {
        let session = connectedSession()

        session.receive(FrameCodec.encode(.ping(time: 12345)))

        XCTAssertEqual(transport.frames().last, .pong(time: 12345))
    }

    func testGarbageBytesEndTheSession() {
        let session = makeSession()

        session.receive([0xFF, 0xFF, 0xFF])

        XCTAssertTrue(session.isEnded)
    }

    func testAHelloSplitAcrossTwoReceivesStillConnects() {
        let session = makeSession()
        let hello = FrameCodec.encode(.hello(version: ProtocolConstants.version))

        session.receive(Array(hello.prefix(1)))
        session.receive(Array(hello.dropFirst()))

        XCTAssertTrue(session.isLive)
    }

    func testBytesThatArriveWhileTheOwnerIsBeingAskedAreHandledAfterHelloAck() {
        let box = SessionBox()
        let session = makeSession(ask: { _ in
            box.session?.receive(FrameCodec.encode(.ping(time: 9)))
            return true
        })
        box.session = session

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        let frames = transport.frames()
        XCTAssertEqual(frames.first, .helloAck(version: ProtocolConstants.version))
        XCTAssertEqual(frames.last, .pong(time: 9))
    }

    func testMoreThanTheCapBufferedWhileTheOwnerIsBeingAskedEndsTheSession() {
        let box = SessionBox()
        var asked = 0
        var endedAtTheCap = true
        let cap = 64 * 1024
        let session = makeSession(ask: { _ in
            asked += 1
            box.session?.receive([UInt8](repeating: 0, count: cap))
            endedAtTheCap = box.session?.isEnded ?? false
            box.session?.receive([0])
            return true
        })
        box.session = session

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        XCTAssertFalse(endedAtTheCap)
        XCTAssertTrue(session.isEnded)
        XCTAssertEqual(asked, 1)
        XCTAssertEqual(transport.sent.count, 0)
        XCTAssertEqual(transport.closeCount, 1)
    }

    func testEndingReleasesAllInputAndClosesTheTransportOnce() {
        let session = connectedSession()

        session.end()
        session.end()

        XCTAssertEqual(input.calls.filter { $0 == "releaseAll" }.count, 1)
        XCTAssertEqual(transport.closeCount, 1)
        XCTAssertEqual(ended.count, 1)
    }

    func testADeadTransportEndsTheSession() {
        let session = makeSession()
        transport.alive = false

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        XCTAssertTrue(session.isEnded)
        XCTAssertFalse(session.isLive)
    }

    func testPollSendsNothingWhenNothingChanged() {
        let session = connectedSession()
        let before = transport.sent.count

        session.poll()

        XCTAssertEqual(transport.sent.count, before)
    }

    func testPollSendsTheNewVolumeWhenItChanged() {
        let session = connectedSession()
        speakers.level = Level(percent: 55, muted: false)

        session.poll()

        XCTAssertEqual(transport.frames().last, .state(control: ControlId.volume.rawValue, value: 55, flags: 0))
    }

    func testPollSendsTheNewBrightnessWhenItChanged() {
        let session = connectedSession()
        brightness.level = 20

        session.poll()

        XCTAssertEqual(transport.frames().last, .state(control: ControlId.brightness.rawValue, value: 20, flags: 0))
    }

    func testPollDoesNothingBeforeTheHandshake() {
        let session = makeSession()

        session.poll()

        XCTAssertEqual(transport.sent.count, 0)
    }

    func testHelloTimedOutEndsASilentSession() {
        let session = makeSession()

        session.helloTimedOut()

        XCTAssertTrue(session.isEnded)
    }

    func testHelloTimedOutLeavesALiveSessionAlone() {
        let session = connectedSession()

        session.helloTimedOut()

        XCTAssertFalse(session.isEnded)
    }

    func testASessionEndedWhileTheOwnerIsBeingAskedSendsNoHelloAck() {
        let box = SessionBox()
        let session = makeSession(ask: { _ in
            box.session?.end()
            return true
        })
        box.session = session

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        XCTAssertEqual(transport.sent.count, 0)
        XCTAssertFalse(session.isLive)
    }

    func testASessionEndedBeforeItsHelloIsHandledIsNeverAsked() {
        var asked = 0
        let session = makeSession(ask: { _ in
            asked += 1
            return true
        })

        session.end()
        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        XCTAssertEqual(asked, 0)
        XCTAssertTrue(admissions.isEmpty)
        XCTAssertEqual(transport.sent.count, 0)
        // Not taken as the owner declining it, and not left asking: the next phone is asked as usual.
        XCTAssertFalse(trust.refuses(phone))
    }

    func testASessionEndedUnderAnOpenQuestionIsRefusedEvenOnAYes() {
        let box = SessionBox()
        var asked = 0
        let session = makeSession(ask: { _ in
            asked += 1
            box.session?.end()
            return true
        })
        box.session = session

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        XCTAssertEqual(asked, 1)
        XCTAssertEqual(askedWith?.isCancelled, true)
        XCTAssertTrue(admissions.isEmpty)
        XCTAssertEqual(transport.sent.count, 0)
        XCTAssertTrue(trust.trustedPhones.isEmpty)
        XCTAssertFalse(trust.refuses(phone))
    }

    func testTheHelloTimeoutWhileTheOwnerIsBeingAskedDoesNotEndTheSession() {
        let box = SessionBox()
        let session = makeSession(ask: { _ in
            box.session?.helloTimedOut()
            return true
        })
        box.session = session

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        XCTAssertTrue(session.isLive)
        XCTAssertFalse(session.isEnded)
    }

    func testMalformedBytesThatArrivedDuringTheQuestionEndTheSessionAfterHelloAck() {
        let box = SessionBox()
        let session = makeSession(ask: { _ in
            box.session?.receive([0xFF])
            return true
        })
        box.session = session

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        XCTAssertEqual(transport.frames().first, .helloAck(version: ProtocolConstants.version))
        XCTAssertTrue(session.isEnded)
    }

    func testASecondHelloOnALiveSessionIsDroppedAndCounted() {
        let session = connectedSession()

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        XCTAssertEqual(dispatcher.dropped, 1)
        XCTAssertFalse(session.isEnded)
    }

    func testAProtocolErrorMidSessionReleasesAllInput() {
        let session = connectedSession()

        session.receive([0xFF])

        XCTAssertTrue(session.isEnded)
        XCTAssertTrue(input.calls.contains("releaseAll"))
    }

    func testAskingForIconsIsNotCountedAsDropped() {
        let session = connectedSession()

        session.receive(FrameCodec.encode(.text(kind: TextKind.wantIcons.rawValue, value: "")))

        XCTAssertEqual(dispatcher.dropped, 0)
        XCTAssertFalse(session.isEnded)
    }

    private func macroStore(icons: MacroIconSource? = nil, windows: MacroWindows? = nil) -> MacroStore {
        let path = directory.appendingPathComponent("macros.txt").path
        let store = MacroStore(path: path, icons: icons, windows: windows)
        dispatcher.macros = store
        return store
    }

    /// The tick armed last, as the timer would fire it.
    private func fireWatchTick() {
        let ticks = watchTicks
        watchTicks = []
        for armed in ticks {
            armed.tick()
        }
    }

    private func macroOpenTexts() -> [String] {
        transport.frames().compactMap { frame -> String? in
            guard case .text(let kind, let value) = frame, kind == TextKind.macroOpen.rawValue else { return nil }
            return value
        }
    }

    private func watchFrame(_ action: ActionId) -> [UInt8] {
        FrameCodec.encode(.runAction(id: action.rawValue))
    }

    func testRunningIsNamedOnlyWhereThisMacCanSeeApps() {
        _ = macroStore(windows: FakeWindows())
        _ = connectedSession()

        XCTAssertTrue(transport.frames().contains(.text(kind: TextKind.features.rawValue, value: "capture/running")))
    }

    func testRunningIsNotNamedWhereThisMacCannotSeeApps() {
        let windows = FakeWindows()
        windows.available = false
        _ = macroStore(windows: windows)
        let session = connectedSession()

        session.receive(watchFrame(.macrosWatch))

        XCTAssertTrue(transport.frames().contains(.text(kind: TextKind.features.rawValue, value: "capture")))
        XCTAssertEqual(macroOpenTexts(), [])
        XCTAssertEqual(dispatcher.dropped, 1)
    }

    func testTheWatchSendsOneCharacterPerSlotAtOnceAndOnlyAChangeAfter() {
        let windows = FakeWindows()
        windows.state = { $0.name == "Mail" ? .front : .closed }
        let store = macroStore(windows: windows)
        store.save([Macro(name: "Mail", target: "Mail.app"), Macro(name: "Code", target: "Code.app")])
        let session = connectedSession()
        XCTAssertEqual(macroOpenTexts(), [])

        session.receive(watchFrame(.macrosWatch))
        XCTAssertEqual(macroOpenTexts(), ["20"])
        XCTAssertEqual(watchTicks.map { $0.seconds }, [1])

        fireWatchTick()
        XCTAssertEqual(macroOpenTexts(), ["20"])

        windows.state = { _ in .open }
        fireWatchTick()
        XCTAssertEqual(macroOpenTexts(), ["20", "11"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testUnwatchStopsTheSurveyAndAWatchHearsTheStateAfresh() {
        let windows = FakeWindows()
        let store = macroStore(windows: windows)
        store.save([Macro(name: "Mail", target: "Mail.app")])
        let session = connectedSession()
        session.receive(watchFrame(.macrosWatch))

        session.receive(watchFrame(.macrosUnwatch))
        windows.state = { _ in .open }
        fireWatchTick()
        XCTAssertEqual(macroOpenTexts(), ["0"])
        XCTAssertTrue(watchTicks.isEmpty)

        windows.state = { _ in .closed }
        session.receive(watchFrame(.macrosWatch))
        XCTAssertEqual(macroOpenTexts(), ["0", "0"])
    }

    func testAnEndedSessionSurveysNoMore() {
        let windows = FakeWindows()
        let store = macroStore(windows: windows)
        store.save([Macro(name: "Mail", target: "Mail.app")])
        let session = connectedSession()
        session.receive(watchFrame(.macrosWatch))
        session.end()
        let surveys = windows.surveys

        fireWatchTick()

        XCTAssertEqual(windows.surveys, surveys)
        XCTAssertTrue(watchTicks.isEmpty)
        XCTAssertNil(dispatcher.macroWatch)
    }

    func testARepeatedWatchSkipsTheSurveyAndTheNextTickReportsAfresh() {
        let windows = FakeWindows()
        let store = macroStore(windows: windows)
        store.save([Macro(name: "Mail", target: "Mail.app")])
        let session = connectedSession()
        session.receive(watchFrame(.macrosWatch))
        let surveys = windows.surveys

        session.receive(watchFrame(.macrosWatch))
        session.receive(watchFrame(.macrosWatch))

        XCTAssertEqual(windows.surveys, surveys)
        XCTAssertEqual(watchTicks.count, 1)
        XCTAssertEqual(macroOpenTexts(), ["0"])

        fireWatchTick()
        XCTAssertEqual(windows.surveys, surveys + 1)
        XCTAssertEqual(macroOpenTexts(), ["0", "0"])
        XCTAssertEqual(watchTicks.count, 1)
    }

    func testAWatchAfterAnUnwatchInTheSameTickSurveysAtOnceWithOneTickArmed() {
        let windows = FakeWindows()
        let store = macroStore(windows: windows)
        store.save([Macro(name: "Mail", target: "Mail.app")])
        let session = connectedSession()
        session.receive(watchFrame(.macrosWatch))

        session.receive(watchFrame(.macrosUnwatch))
        session.receive(watchFrame(.macrosWatch))
        XCTAssertEqual(macroOpenTexts(), ["0", "0"])

        fireWatchTick()
        XCTAssertEqual(watchTicks.count, 1)
    }

    func testTheMacroNamesAreSentAtTheHandshakeAndAgainWhenTheyChange() {
        let store = macroStore()
        store.save([Macro(name: "Mail", target: "Mail.app")])
        let session = connectedSession()

        store.save([Macro(name: "Mail", target: "Mail.app"), Macro(name: "Code", target: "Code.app")])

        let names = transport.frames().compactMap { frame -> String? in
            guard case .text(let kind, let value) = frame, kind == TextKind.macros.rawValue else { return nil }
            return value
        }
        XCTAssertEqual(names, ["Mail", "Mail/Code"])
        XCTAssertFalse(session.isEnded)
    }

    func testAnEndedSessionHearsNoMoreChanges() {
        let store = macroStore()
        let session = connectedSession()
        session.end()
        let sent = transport.sent.count

        store.save([Macro(name: "Mail", target: "Mail.app")])

        XCTAssertEqual(transport.sent.count, sent)
    }

    func testAskingForIconsSendsEachSlotsChunks() {
        let png = [UInt8](repeating: 7, count: 400)
        let store = macroStore(icons: FakeIcons(bytes: png))
        store.save([Macro(name: "Mail", target: "Mail.app"), Macro(name: "Code", target: "Code.app")])
        let session = connectedSession()
        let before = transport.frames().count

        session.receive(FrameCodec.encode(.text(kind: TextKind.wantIcons.rawValue, value: "")))

        let expected = [0, 1].flatMap { MacroIconChunks.chunks(slot: $0, png: png) }
            .map { Frame.text(kind: TextKind.macroIcon.rawValue, value: $0) }
        XCTAssertEqual(framesSince(before), expected)
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testAFrameAfterAskingForIconsIsHandledBeforeAnyIconAndANewerAskSupersedes() {
        let png = [UInt8](repeating: 7, count: 400)
        let store = macroStore(icons: FakeIcons(bytes: png))
        store.save([Macro(name: "Mail", target: "Mail.app"), Macro(name: "Code", target: "Code.app")])
        holdsIcons = true
        let session = connectedSession()
        let before = transport.frames().count
        let wantIcons = FrameCodec.encode(.text(kind: TextKind.wantIcons.rawValue, value: ""))

        session.receive(wantIcons)
        session.receive(FrameCodec.encode(.ping(time: 3)))
        XCTAssertEqual(framesSince(before), [.pong(time: 3)])

        // The first run's turn is still queued; only the second run's icons may go out, once each.
        session.receive(wantIcons)
        runIconTurns()

        let icons = [0, 1].flatMap { MacroIconChunks.chunks(slot: $0, png: png) }
            .map { Frame.text(kind: TextKind.macroIcon.rawValue, value: $0) }
        XCTAssertEqual(framesSince(before), [.pong(time: 3)] + icons)
    }

    func testAnEndedSessionSendsNoQueuedIcon() {
        let store = macroStore(icons: FakeIcons(bytes: [UInt8](repeating: 7, count: 400)))
        store.save([Macro(name: "Mail", target: "Mail.app")])
        holdsIcons = true
        let session = connectedSession()
        session.receive(FrameCodec.encode(.text(kind: TextKind.wantIcons.rawValue, value: "")))
        session.end()
        let sent = transport.sent.count

        runIconTurns()

        XCTAssertEqual(transport.sent.count, sent)
    }

    /// A live session on a link capped like the real outboxes, with two macros whose icons each outweigh it unpaced.
    private func connectedOverACappedLink(png: [UInt8]) -> Session {
        let store = macroStore(icons: FakeIcons(bytes: png))
        store.save([Macro(name: "Mail", target: "Mail.app"), Macro(name: "Code", target: "Code.app")])
        let session = connectedSession()
        heldSession = session
        transport.cap = 64 * 1024
        return session
    }

    private func wantIcons(_ size: Int) -> [UInt8] {
        FrameCodec.encode(.text(kind: TextKind.wantIcons.rawValue, value: String(size)))
    }

    /// Lets the link drain and fires the ticks armed, until none is.
    private func drainTheLink() {
        while !watchTicks.isEmpty {
            transport.backlog = 0
            fireWatchTick()
        }
    }

    func testAnIconHeavierThanTheOutboxIsPacedAndArrivesWhole() {
        let png = [UInt8](repeating: 7, count: MacroIconChunks.maxBytes(size: 512))
        let session = connectedOverACappedLink(png: png)
        let before = transport.frames().count

        session.receive(wantIcons(512))

        XCTAssertFalse(session.isEnded)
        XCTAssertEqual(watchTicks.map { $0.seconds }, [CaptureToPhone.tick])
        XCTAssertLessThan(transport.backlog, 64 * 1024)
        drainTheLink()

        XCTAssertFalse(session.isEnded)
        let icons = [0, 1].flatMap { MacroIconChunks.chunks(slot: $0, png: png) }
            .map { Frame.text(kind: TextKind.macroIcon.rawValue, value: $0) }
        XCTAssertEqual(framesSince(before), icons)
    }

    func testANewerAskSupersedesARunWaitingOnTheLink() {
        let old = [UInt8](repeating: 7, count: MacroIconChunks.maxBytes(size: 512))
        let session = connectedOverACappedLink(png: old)
        let before = transport.frames().count
        session.receive(wantIcons(512))
        let partWay = framesSince(before)

        session.receive(wantIcons(512))
        drainTheLink()

        let whole = [0, 1].flatMap { MacroIconChunks.chunks(slot: $0, png: old) }
            .map { Frame.text(kind: TextKind.macroIcon.rawValue, value: $0) }
        XCTAssertEqual(framesSince(before), partWay + whole)
        XCTAssertFalse(session.isEnded)
    }

    func testTheSessionsEndStopsARunWaitingOnTheLink() {
        let session = connectedOverACappedLink(png: [UInt8](repeating: 7, count: MacroIconChunks.maxBytes(size: 512)))
        session.receive(wantIcons(512))
        XCTAssertFalse(watchTicks.isEmpty)
        session.end()
        let sent = transport.sent.count

        drainTheLink()

        XCTAssertEqual(transport.sent.count, sent)
    }

    func testASaveStopsARunWaitingOnTheLink() {
        let session = connectedOverACappedLink(png: [UInt8](repeating: 7, count: MacroIconChunks.maxBytes(size: 512)))
        session.receive(wantIcons(512))
        dispatcher.macros?.save([Macro(name: "Mail", target: "Mail.app")])
        let sent = transport.sent.count

        drainTheLink()

        XCTAssertEqual(transport.sent.count, sent)
    }

    func testAFullLinkHoldsABlobBackUntilItDrains() {
        let data: [UInt8] = [1, 2, 3]
        let (_, clipboard, _) = sharing(data)
        transport.backlog = CaptureToPhone.maxBacklog

        clipboard.localChanged()

        XCTAssertEqual(blobTicks.count, 1)
        XCTAssertEqual(transport.frames().last, .blobBegin(kind: BlobKind.clipText.rawValue, size: 3, label: ""))

        transport.backlog = 0
        blobTicks.removeFirst()()

        XCTAssertEqual(transport.frames().last, .blobEnd(sha256: Array(SHA256.hash(data: data))))
    }

    func testAskingForIconsWithNoPictureSendsNothing() {
        _ = macroStore()
        let session = connectedSession()
        let before = transport.frames().count

        session.receive(FrameCodec.encode(.text(kind: TextKind.wantIcons.rawValue, value: "")))

        XCTAssertEqual(framesSince(before), [])
    }

    // The PIN gate: the cases of the Windows PinGateTests, through the session.

    func testLevelOneSendsNoPinFrameAndArmsNoTimeout() {
        let session = connectedSession()

        // The rest of what is sent is pinned down by testAnAcceptedPhoneIsGivenHelloAckThenTheStateReport.
        XCTAssertEqual(transport.frames().first, .helloAck(version: ProtocolConstants.version))
        XCTAssertTrue(timeouts.isEmpty)
        XCTAssertTrue(session.isLive)
    }

    func testLevelThreeAsksForThePinEveryTimeBeforeTheServerOrHelloAck() {
        setLevel(3)
        for _ in 0..<2 {
            let before = transport.frames().count
            let session = makeSession()

            session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
            XCTAssertEqual(framesSince(before), [question(5)])
            XCTAssertFalse(session.isLive)

            session.receive(answer("123456"))
            XCTAssertTrue(session.isLive)
            XCTAssertEqual(framesSince(before).dropFirst().first, .helloAck(version: ProtocolConstants.version))
        }
        XCTAssertEqual(admissions.count, 2)
    }

    func testLevelTwoAsksOnce() {
        setLevel(2)
        let first = makeSession()
        first.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
        first.receive(answer("123456"))
        XCTAssertTrue(first.isLive)

        let before = transport.frames().count
        _ = connectedSession()

        XCTAssertEqual(framesSince(before).first, .helloAck(version: ProtocolConstants.version))
    }

    func testAWrongPinIsAskedAgainWithFewerTriesAndARightOneProceeds() {
        setLevel(3)
        let session = makeSession()

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
        session.receive(answer("654321"))
        session.receive(answer("654321"))
        session.receive(answer("123456"))

        XCTAssertEqual(
            Array(transport.frames().prefix(4)),
            [question(5), question(4), question(3), .helloAck(version: ProtocolConstants.version)])
        XCTAssertTrue(session.isLive)
        XCTAssertEqual(pins.attemptsLeft(phone), 5)
    }

    func testFiveWrongPinsLockAndCloseUntilTheLockRunsOut() {
        setLevel(3)
        var asked = 0
        let ask: (String) -> Bool = { _ in
            asked += 1
            return true
        }
        let session = makeSession(ask: ask)

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
        for _ in 0..<5 {
            session.receive(answer("654321"))
        }

        XCTAssertEqual(
            transport.frames(),
            [question(5), question(4), question(3), question(2), question(1), question(0, locked: 30)])
        XCTAssertTrue(session.isEnded)
        XCTAssertEqual(admissions, [])
        XCTAssertEqual(statuses.last, "Refused a phone that did not give the right PIN")

        // The next connection is turned away before the trust question, with what is left of the wait.
        time += 10
        var before = transport.frames().count
        let refused = makeSession(ask: ask)
        refused.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        XCTAssertEqual(framesSince(before), [question(0, locked: 20)])
        XCTAssertTrue(refused.isEnded)
        XCTAssertEqual(asked, 1)
        XCTAssertEqual(statuses.last, "Refused a phone locked out after wrong PINs")

        time += 20
        before = transport.frames().count
        let again = makeSession(ask: ask)
        again.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
        again.receive(answer("123456"))

        XCTAssertEqual(framesSince(before).first, question(5))
        XCTAssertTrue(again.isLive)
    }

    func testALockOutLeftFromAHigherLevelSendsNothingAtLevelOne() {
        setLevel(3)
        let session = makeSession()
        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
        for _ in 0..<5 {
            session.receive(answer("654321"))
        }
        XCTAssertTrue(pins.setLevel(1))

        let before = transport.frames().count
        _ = connectedSession()

        XCTAssertEqual(framesSince(before).first, .helloAck(version: ProtocolConstants.version))
    }

    func testAnyOtherFrameWhileWaitingForThePinEndsTheSession() {
        setLevel(3)
        let session = makeSession()

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
        session.receive(FrameCodec.encode(.ping(time: 1)))

        XCTAssertEqual(transport.frames(), [question(5)])
        XCTAssertTrue(session.isEnded)
        XCTAssertEqual(admissions, [])
        XCTAssertEqual(pins.attemptsLeft(phone), 5)
    }

    func testNoAnswerWithinTheTimeoutEndsTheSession() {
        setLevel(3)
        let session = makeSession()

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
        XCTAssertEqual(timeouts.count, 1)
        timeouts[0]()

        XCTAssertTrue(session.isEnded)
        XCTAssertFalse(session.isLive)
    }

    func testEachQuestionGetsItsOwnTimeout() {
        setLevel(3)
        let session = makeSession()

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
        session.receive(answer("654321"))
        XCTAssertEqual(timeouts.count, 2)

        // The first question was answered, so its timer firing late changes nothing.
        timeouts[0]()
        XCTAssertFalse(session.isEnded)
        timeouts[1]()
        XCTAssertTrue(session.isEnded)
    }

    func testTheHelloTimeoutDoesNotEndTheWaitForThePin() {
        setLevel(3)
        let session = makeSession()

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
        session.helloTimedOut()

        XCTAssertFalse(session.isEnded)
    }

    func testUnreadableSettingsCloseWithoutCountingTheAnswer() throws {
        setLevel(3)
        let session = makeSession()
        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        try "nonsense\n".write(
            toFile: directory.appendingPathComponent("security.txt").path, atomically: true, encoding: .utf8)
        session.receive(answer("123456"))

        XCTAssertEqual(transport.frames(), [question(5)])
        XCTAssertTrue(session.isEnded)
        XCTAssertEqual(pins.attemptsLeft(phone), 5)
    }

    func testAPinAnswerAfterHelloAckIsDroppedAndCounted() {
        setLevel(3)
        let session = makeSession()
        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
        session.receive(answer("123456"))

        session.receive(answer("123456"))

        XCTAssertEqual(dispatcher.dropped, 1)
        XCTAssertFalse(session.isEnded)
    }

    func testBytesThatArriveWhileTheOwnerIsBeingAskedReachThePinQuestionInArrivalOrder() {
        setLevel(3)
        let box = SessionBox()
        let pinThenPing = answer("123456") + FrameCodec.encode(.ping(time: 9))
        let session = makeSession(ask: { _ in
            box.session?.receive(pinThenPing)
            return true
        })
        box.session = session

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        let frames = transport.frames()
        XCTAssertEqual(Array(frames.prefix(2)), [question(5), .helloAck(version: ProtocolConstants.version)])
        XCTAssertEqual(frames.last, .pong(time: 9))
        XCTAssertTrue(session.isLive)
    }

    /// A live session with sharing turned on and `content` on the clipboard, about to be sent.
    private func sharing(_ content: [UInt8]) -> (Session, ClipboardSync, FakeClipboard) {
        let surface = FakeClipboard()
        surface.current = ClipboardContent(kind: .clipText, data: content)
        let clipboard = ClipboardSync(
            surface: surface,
            sender: BlobSender(
                link: .bluetooth,
                schedule: { [weak self] _, tick in self?.blobTicks.append(tick) },
                lastInput: { -.infinity }
            ),
            allowed: { true },
            schedule: { _, work in work() }
        )
        dispatcher.clipboard = clipboard
        let session = connectedSession()
        heldSession = session
        session.receive(FrameCodec.encode(.setValue(control: ControlId.clipboard.rawValue, value: 1)))
        return (session, clipboard, surface)
    }

    func testABlobGoesOutThroughTheSession() {
        let data: [UInt8] = [1, 2, 3]
        let (_, clipboard, _) = sharing(data)

        clipboard.localChanged()

        XCTAssertEqual(
            Array(transport.frames().suffix(3)),
            [
                .blobBegin(kind: BlobKind.clipText.rawValue, size: 3, label: ""),
                .blobChunk(seq: 0, data: data),
                .blobEnd(sha256: Array(SHA256.hash(data: data))),
            ]
        )
    }

    func testABlobFromThePhoneReachesTheClipboardThroughTheSession() {
        let (session, _, surface) = sharing([1])
        let data = Array("hi".utf8)

        session.receive(FrameCodec.encode(.blobBegin(kind: BlobKind.clipText.rawValue, size: 2, label: "")))
        session.receive(FrameCodec.encode(.blobChunk(seq: 0, data: data)))
        session.receive(FrameCodec.encode(.blobEnd(sha256: Array(SHA256.hash(data: data)))))

        XCTAssertEqual(surface.writes.map(\.data), [data])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testADeadLinkEndsTheSessionWhenABlobIsSent() {
        let (session, clipboard, _) = sharing([UInt8](repeating: 7, count: 100 * FrameCodec.maxChunkBytes))
        transport.alive = false

        clipboard.localChanged()

        XCTAssertTrue(session.isEnded)
        XCTAssertTrue(blobTicks.isEmpty)
    }

    func testTheSessionsEndStopsABlobOnItsWay() {
        let (session, clipboard, _) = sharing([UInt8](repeating: 7, count: 100 * FrameCodec.maxChunkBytes))
        clipboard.localChanged()
        let sent = transport.sent.count

        session.end()
        for tick in blobTicks {
            tick()
        }

        XCTAssertEqual(transport.sent.count, sent)
    }

    func testNoLogLineHoldsTheDigits() {
        setLevel(3)
        let offset = logSize()

        let first = makeSession()
        first.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
        first.receive(answer("654321"))
        first.receive(answer("123456"))
        let second = makeSession()
        second.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
        for _ in 0..<5 {
            second.receive(answer("654321"))
        }

        let log = logSince(offset)
        XCTAssertTrue(log.contains("PIN rejected for \(phone)"))
        XCTAssertFalse(log.contains("123456"))
        XCTAssertFalse(log.contains("654321"))
    }
}
