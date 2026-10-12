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

    private lazy var trust = TrustStore(path: directory.appendingPathComponent("trust").path)
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
            askTrust: ask,
            dispatcher: dispatcher,
            input: input,
            speakers: speakers,
            microphone: microphone,
            brightness: brightness,
            display: display,
            onStatus: { [weak self] status in self?.statuses.append(status) },
            onEnded: { [weak self] session in self?.ended.append(session) }
        )
    }

    private func connectedSession() -> Session {
        let session = makeSession()
        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))
        return session
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
                .state(control: ControlId.volume.rawValue, value: 40, flags: 1),
                .state(control: ControlId.micLevel.rawValue, value: 30, flags: 0),
                .state(control: ControlId.brightness.rawValue, value: 70, flags: 0),
                .text(kind: TextKind.refreshRates.rawValue, value: "60/120"),
                .state(control: ControlId.refreshRate.rawValue, value: 1, flags: 0),
                .text(kind: TextKind.padStatus.rawValue, value: "no-driver"),
                .text(kind: TextKind.macros.rawValue, value: ""),
            ]
        )
    }

    func testAnAcceptedPhoneIsReportedAsConnected() {
        _ = connectedSession()

        XCTAssertEqual(statuses, ["Connected to \(phone)"])
    }

    func testAPhoneOtherThanTheTrustedOneIsRefusedWithoutAsking() {
        var asked = false
        _ = trust.admit("11:22:33:44:55:66") { true }
        let session = makeSession(ask: { _ in
            asked = true
            return true
        })

        session.receive(FrameCodec.encode(.hello(version: ProtocolConstants.version)))

        XCTAssertFalse(asked)
        XCTAssertTrue(session.isEnded)
        XCTAssertEqual(transport.sent.count, 0)
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
}
