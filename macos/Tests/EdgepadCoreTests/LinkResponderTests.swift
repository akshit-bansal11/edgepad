import CryptoKit
import XCTest

@testable import EdgepadCore

/// The owner's side of the pairing question, answered by hand.
private final class FakePairingPrompt: PairingPrompt {
    private(set) var shown: [String] = []
    private(set) var withdrawn = 0
    /// What every question was told after the code was typed, in order.
    private(set) var told: [String] = []
    private var typed: ((String) -> Void)?
    private var cancelled: (() -> Void)?

    private final class Question: PairingQuestion {
        weak var owner: FakePairingPrompt?

        init(_ owner: FakePairingPrompt) {
            self.owner = owner
        }

        func matched() {
            owner?.told.append("matched")
        }

        func mismatched() {
            owner?.told.append("mismatched")
        }

        func finished(paired: Bool) {
            owner?.told.append("finished:\(paired)")
        }

        func withdraw() {
            owner?.withdrawn += 1
        }
    }

    func show(
        phoneName: String, onTyped: @escaping (String) -> Void, onCancelled: @escaping () -> Void
    ) -> PairingQuestion {
        shown.append(phoneName)
        typed = onTyped
        cancelled = onCancelled
        return Question(self)
    }

    func type(_ digits: String) {
        typed?(digits)
    }

    func cancel() {
        cancelled?()
    }
}

/// The phone's half of EDGL1, step for step as the C# test `LinkInitiator` has it, driving the Mac's responder.
final class LinkResponderTests: XCTestCase {
    private let directory = URL(fileURLWithPath: NSTemporaryDirectory())
        .appendingPathComponent("edgepad-link-" + UUID().uuidString)
    private let phoneId = Array(UInt8(0)..<16)
    private let identity = LinkIdentity(id: Array(UInt8(16)..<32), name: "Mac")
    private let prompt = FakePairingPrompt()
    private let limiter = PairingLimiter(now: { 0 })

    private lazy var trust = TrustStore(path: directory.appendingPathComponent("trusted-phone.txt").path)
    private lazy var paired = PairedLinks(path: directory.appendingPathComponent("paired-links.txt").path)

    /// What the responder sent, one message each, and its timers, fired by hand.
    private var sent: [[UInt8]] = []
    private var timers: [() -> Void] = []
    private var results: [LinkResult?] = []

    override func tearDown() {
        try? FileManager.default.removeItem(at: directory)
        super.tearDown()
    }

    private func responder() -> LinkResponder {
        sent = []
        timers = []
        results = []
        return LinkResponder(
            identity: identity, paired: paired, limiter: limiter, prompt: prompt, trust: trust,
            send: { [weak self] bytes in
                self?.sent.append(bytes)
                return true
            },
            schedule: { [weak self] _, run in self?.timers.append(run) },
            onDone: { [weak self] result in self?.results.append(result) })
    }

    /// The body of the last message sent.
    private func reply() throws -> [UInt8] {
        let last = try XCTUnwrap(sent.last)
        XCTAssertEqual(Int(last[0]) | Int(last[1]) << 8, last.count - 2)
        return Array(last.dropFirst(2))
    }

    private var address: String { LinkResponder.address(of: phoneId) }

    /// A pairing up to the code on the Mac's screen: the phone's key, nonce, transcript and secret.
    private struct Phone {
        let key: P256.KeyAgreement.PrivateKey
        let nonce: [UInt8]
        let transcript: [UInt8]
        let prk: [UInt8]
        var code: String { LinkCrypto.sas(transcript) }
    }

    private func pairToCode(_ link: LinkResponder, name: String = "Pixel") throws -> Phone {
        let key = LinkCrypto.newKeyPair()
        let nonce = Hex.random(16)
        let publicKey = LinkCrypto.publicKey(key)
        link.receive(
            LinkWire.message(
                LinkWire.pairRequest(phoneId: phoneId, name: name, commit: LinkCrypto.commit(publicKey, nonce))))
        var offer = LinkFieldReader(try reply())
        XCTAssertEqual(try offer.byte(), LinkWire.statusOk)
        XCTAssertEqual(try offer.bytes(16), identity.id)
        let laptopName = try offer.nameField()
        let laptopPublic = try offer.bytes(65)
        let laptopNonce = try offer.bytes(16)
        try offer.end()

        link.receive(LinkWire.message(LinkWire.reveal(publicKey: publicKey, nonce: nonce)))
        let transcript = LinkCrypto.pairTranscript(
            phoneId: phoneId, laptopId: identity.id, phoneName: LinkWire.nameField(name), laptopName: laptopName,
            phonePublic: publicKey, phoneNonce: nonce, laptopPublic: laptopPublic, laptopNonce: laptopNonce)
        let prk = LinkCrypto.pairPrk(transcript, try LinkCrypto.agree(key, laptopPublic))
        return Phone(key: key, nonce: nonce, transcript: transcript, prk: prk)
    }

    func testInfoSaysWhoThisMacIsAndCloses() throws {
        let link = responder()
        link.receive(LinkWire.message(LinkWire.infoRequest()))

        var info = LinkFieldReader(try reply())
        XCTAssertEqual(try info.byte(), LinkWire.statusOk)
        XCTAssertEqual(try info.bytes(16), identity.id)
        XCTAssertEqual(try info.byte(), ProtocolConstants.version)
        XCTAssertEqual(LinkWire.text(try info.nameField()), "Mac")
        XCTAssertEqual(results.count, 1)
        XCTAssertNil(results[0])
    }

    func testTheRightCodeTypedOnTheMacPairsTheOwnersPhone() throws {
        let link = responder()
        let phone = try pairToCode(link)
        XCTAssertEqual(prompt.shown, ["Pixel"])
        XCTAssertTrue(results.isEmpty)

        prompt.type(phone.code)
        XCTAssertEqual(try reply(), LinkCrypto.laptopTag(phone.prk, phone.transcript, pairing: true))
        XCTAssertFalse(trust.trustedPhones.contains(address), "trusted before the phone confirmed")

        // The phone's first record may come in the same packet as its confirmation.
        let record: [UInt8] = [9, 9, 9]
        link.receive(LinkWire.message(LinkCrypto.phoneTag(phone.prk, phone.transcript, pairing: true)) + record)
        let result = try XCTUnwrap(results.first ?? nil)
        XCTAssertEqual(result.address, address)
        XCTAssertEqual(result.phoneName, "Pixel")
        XCTAssertTrue(result.justPaired)
        XCTAssertEqual(result.leftover, record)
        XCTAssertEqual(result.prk, phone.prk)
        XCTAssertEqual(trust.trustedPhones, [address])
        XCTAssertEqual(paired.find(Hex.string(phoneId))?.key, LinkCrypto.pairingKey(phone.prk))
        XCTAssertGreaterThan(prompt.withdrawn, 0)
        XCTAssertEqual(prompt.told, ["matched", "finished:true"])
    }

    func testAWrongCodeIsAFailureAndNothingIsTrusted() throws {
        let link = responder()
        let phone = try pairToCode(link)
        let sentBefore = sent.count

        prompt.type(phone.code == "000000" ? "000001" : "000000")

        XCTAssertEqual(sent.count, sentBefore, "the Mac confirmed after a wrong code")
        XCTAssertEqual(prompt.told, ["mismatched"])
        XCTAssertEqual(results.count, 1)
        XCTAssertNil(results[0])
        XCTAssertTrue(trust.trustedPhones.isEmpty)
        XCTAssertNil(paired.find(Hex.string(phoneId)))
    }

    func testARogueThatConfirmsWithoutTheOwnerTypingIsNotPaired() throws {
        let link = responder()
        let phone = try pairToCode(link)

        // It can work out every value itself; what it cannot do is type on this Mac.
        link.receive(LinkWire.message(LinkCrypto.phoneTag(phone.prk, phone.transcript, pairing: true)))

        XCTAssertEqual(results.count, 1)
        XCTAssertNil(results[0])
        XCTAssertTrue(trust.trustedPhones.isEmpty)
        XCTAssertNil(paired.find(Hex.string(phoneId)))
        prompt.type(phone.code)
        XCTAssertEqual(results.count, 1)
        XCTAssertTrue(trust.trustedPhones.isEmpty)
    }

    func testNoCodeWithinTheMinuteIsARefusal() throws {
        let link = responder()
        _ = try pairToCode(link)

        for timer in timers {
            timer()
        }

        XCTAssertEqual(results.count, 1)
        XCTAssertNil(results[0])
        XCTAssertTrue(trust.trustedPhones.isEmpty)
    }

    func testAPhoneThatDoesNotConfirmTheSameKeysIsNotPaired() throws {
        let link = responder()
        let phone = try pairToCode(link)
        prompt.type(phone.code)

        link.receive(LinkWire.message([UInt8](repeating: 7, count: 32)))

        XCTAssertEqual(prompt.told, ["matched", "finished:false"])
        XCTAssertNil(try XCTUnwrap(results.first))
        XCTAssertTrue(trust.trustedPhones.isEmpty)
        XCTAssertNil(paired.find(Hex.string(phoneId)))
    }

    func testAKeyThatDoesNotOpenTheCommitmentIsRefused() throws {
        let link = responder()
        let committed = LinkCrypto.publicKey(LinkCrypto.newKeyPair())
        let nonce = Hex.random(16)
        link.receive(
            LinkWire.message(
                LinkWire.pairRequest(phoneId: phoneId, name: "Pixel", commit: LinkCrypto.commit(committed, nonce))))

        let other = LinkCrypto.publicKey(LinkCrypto.newKeyPair())
        link.receive(LinkWire.message(LinkWire.reveal(publicKey: other, nonce: nonce)))

        XCTAssertNil(try XCTUnwrap(results.first))
        XCTAssertTrue(prompt.shown.isEmpty)
    }

    func testAKeyOffTheCurveIsRefusedEvenWhenItOpensTheCommitment() throws {
        let link = responder()
        var bad = LinkCrypto.publicKey(LinkCrypto.newKeyPair())
        bad[64] ^= 0x01
        let nonce = Hex.random(16)
        link.receive(
            LinkWire.message(
                LinkWire.pairRequest(phoneId: phoneId, name: "Pixel", commit: LinkCrypto.commit(bad, nonce))))
        link.receive(LinkWire.message(LinkWire.reveal(publicKey: bad, nonce: nonce)))

        XCTAssertNil(try XCTUnwrap(results.first))
        XCTAssertTrue(prompt.shown.isEmpty)
    }

    /// The code a relay in the middle would need to match on both legs hangs on the nonce the phone reveals only after
    /// it has seen the Mac's, so the relay cannot choose it.
    func testTheCodeDependsOnTheNonceRevealedAfterTheCommitment() throws {
        let first = try pairToCode(responder())
        let laptopSide = LinkCrypto.pairTranscript(
            phoneId: phoneId, laptopId: identity.id, phoneName: LinkWire.nameField("Pixel"),
            laptopName: LinkWire.nameField("Mac"), phonePublic: LinkCrypto.publicKey(first.key),
            phoneNonce: Hex.random(16), laptopPublic: LinkCrypto.publicKey(LinkCrypto.newKeyPair()),
            laptopNonce: Hex.random(16))

        XCTAssertNotEqual(laptopSide, first.transcript)
    }

    // Each responder is held for its whole pairing, as the app holds it: let go of, its prompt callbacks go nowhere.
    func testTheOwnerCancellingDoesNotCountTowardTheLock() throws {
        for _ in 0..<6 {
            let link = responder()
            _ = try pairToCode(link)
            prompt.cancel()
            XCTAssertNil(try XCTUnwrap(results.first))
        }
        XCTAssertTrue(prompt.told.isEmpty)
        XCTAssertTrue(limiter.tryBegin())
    }

    func testFiveWrongCodesLockPairingAndABusyPhoneIsTold() throws {
        for _ in 0..<5 {
            let link = responder()
            let phone = try pairToCode(link)
            prompt.type(phone.code == "000000" ? "000001" : "000000")
            XCTAssertNil(try XCTUnwrap(results.first))
        }

        let link = responder()
        link.receive(LinkWire.message(LinkWire.pairRequest(phoneId: phoneId, name: "Pixel", commit: Hex.random(32))))

        XCTAssertEqual(try reply(), [LinkWire.statusBusy])
        XCTAssertNil(try XCTUnwrap(results.first))
    }

    /// Otherwise a peer could put the pairing question on the owner's screen again and again by hanging up under it.
    func testAPhoneThatHangsUpWhileTheOwnerIsAskedCountsTowardTheLock() throws {
        for _ in 0..<5 {
            let link = responder()
            _ = try pairToCode(link)
            link.cancel(byPeer: true)
            XCTAssertNil(try XCTUnwrap(results.first))
            XCTAssertGreaterThan(prompt.withdrawn, 0)
        }

        XCTAssertFalse(limiter.tryBegin())
    }

    func testAForgetWhileTheOwnerIsAskedKeepsAndTrustsNothing() throws {
        let link = responder()
        let phone = try pairToCode(link)
        let sentBefore = sent.count

        XCTAssertTrue(trust.forget())
        XCTAssertTrue(paired.forget())
        prompt.type(phone.code)

        XCTAssertEqual(sent.count, sentBefore, "the Mac confirmed a pairing begun before the Forget")
        XCTAssertEqual(prompt.told, ["finished:false"])
        XCTAssertNil(try XCTUnwrap(results.first))
        XCTAssertTrue(trust.trustedPhones.isEmpty)
        XCTAssertNil(paired.find(Hex.string(phoneId)))
        XCTAssertTrue(limiter.tryBegin(), "the owner's Forget counted against the lock")
    }

    func testAForgetBeforeThePhoneConfirmsKeepsAndTrustsNothing() throws {
        let link = responder()
        let phone = try pairToCode(link)
        prompt.type(phone.code)

        XCTAssertTrue(trust.forget())
        link.receive(LinkWire.message(LinkCrypto.phoneTag(phone.prk, phone.transcript, pairing: true)))

        XCTAssertEqual(prompt.told, ["matched", "finished:false"])
        XCTAssertNil(try XCTUnwrap(results.first))
        XCTAssertTrue(trust.trustedPhones.isEmpty)
        XCTAssertNil(paired.find(Hex.string(phoneId)))
        XCTAssertFalse(trust.grant(address, since: trust.forgetGeneration - 1))
    }

    func testAForgottenPairingsFileRefusesALateAdd() {
        let generation = paired.generation
        XCTAssertTrue(paired.forget())

        XCTAssertFalse(paired.add(Hex.string(phoneId), key: Hex.random(32), name: "Pixel", since: generation))
        XCTAssertNil(paired.find(Hex.string(phoneId)))
    }

    func testAPairedPhoneResumes() throws {
        let pairingKey = Hex.random(32)
        XCTAssertTrue(paired.add(Hex.string(phoneId), key: pairingKey, name: "Pixel", since: paired.generation))
        let link = responder()
        let key = LinkCrypto.newKeyPair()
        let nonce = Hex.random(16)
        link.receive(
            LinkWire.message(
                LinkWire.resumeRequest(phoneId: phoneId, publicKey: LinkCrypto.publicKey(key), nonce: nonce)))

        var answer = LinkFieldReader(try reply())
        XCTAssertEqual(try answer.byte(), LinkWire.statusOk)
        let laptopPublic = try answer.bytes(65)
        let laptopNonce = try answer.bytes(16)
        let tagL = try answer.bytes(32)
        let th = LinkCrypto.resumeTranscript(
            phoneId: phoneId, laptopId: identity.id, phonePublic: LinkCrypto.publicKey(key), phoneNonce: nonce,
            laptopPublic: laptopPublic, laptopNonce: laptopNonce)
        let prk = LinkCrypto.resumePrk(th, try LinkCrypto.agree(key, laptopPublic), pairingKey)
        XCTAssertEqual(tagL, LinkCrypto.laptopTag(prk, th, pairing: false))

        link.receive(LinkWire.message(LinkCrypto.phoneTag(prk, th, pairing: false)))
        let result = try XCTUnwrap(results.first ?? nil)
        XCTAssertFalse(result.justPaired)
        XCTAssertEqual(result.phoneName, "Pixel")
        XCTAssertEqual(result.prk, prk)
    }

    func testAnUnknownPhoneIsToldToPairAgain() throws {
        let link = responder()
        link.receive(
            LinkWire.message(
                LinkWire.resumeRequest(
                    phoneId: phoneId, publicKey: LinkCrypto.publicKey(LinkCrypto.newKeyPair()), nonce: Hex.random(16))))

        XCTAssertEqual(try reply(), [LinkWire.statusUnknownPhone])
        XCTAssertNil(try XCTUnwrap(results.first))
    }

    func testAPhoneWithTheWrongKeyFailsBothTags() throws {
        XCTAssertTrue(paired.add(Hex.string(phoneId), key: Hex.random(32), name: "Pixel", since: paired.generation))
        let link = responder()
        let key = LinkCrypto.newKeyPair()
        let nonce = Hex.random(16)
        link.receive(
            LinkWire.message(
                LinkWire.resumeRequest(phoneId: phoneId, publicKey: LinkCrypto.publicKey(key), nonce: nonce)))
        var answer = LinkFieldReader(try reply())
        _ = try answer.byte()
        let laptopPublic = try answer.bytes(65)
        let laptopNonce = try answer.bytes(16)
        let tagL = try answer.bytes(32)
        let th = LinkCrypto.resumeTranscript(
            phoneId: phoneId, laptopId: identity.id, phonePublic: LinkCrypto.publicKey(key), phoneNonce: nonce,
            laptopPublic: laptopPublic, laptopNonce: laptopNonce)
        let wrong = LinkCrypto.resumePrk(th, try LinkCrypto.agree(key, laptopPublic), Hex.random(32))
        XCTAssertNotEqual(tagL, LinkCrypto.laptopTag(wrong, th, pairing: false))

        // Sent anyway, which a real phone never does, so the Mac's own check is what refuses it.
        link.receive(LinkWire.message(LinkCrypto.phoneTag(wrong, th, pairing: false)))
        XCTAssertNil(try XCTUnwrap(results.first))
    }

    func testAnotherLinkVersionIsRefusedWithoutReadingFurther() throws {
        let link = responder()
        link.receive(LinkWire.message(LinkWire.magic + [9, 0, 1, 2, 3]))

        XCTAssertEqual(try reply(), [LinkWire.statusUnsupported])
        XCTAssertNil(try XCTUnwrap(results.first))
    }

    func testAMessageOverTheLimitIsRefused() {
        let link = responder()
        link.receive([0xFF, 0xFF])

        XCTAssertEqual(results.count, 1)
        XCTAssertTrue(sent.isEmpty)
    }

    func testThisMacStoppingAPairingWithdrawsTheQuestionUncounted() throws {
        let link = responder()
        _ = try pairToCode(link)

        link.cancel(byPeer: false)

        XCTAssertNil(try XCTUnwrap(results.first))
        XCTAssertGreaterThan(prompt.withdrawn, 0)
        XCTAssertTrue(limiter.tryBegin())
    }
}

final class PairingLimiterTests: XCTestCase {
    func testFiveFailuresLockForFiveMinutesThenTen() {
        var now: TimeInterval = 0
        let limiter = PairingLimiter(now: { now })
        for _ in 0..<5 {
            XCTAssertTrue(limiter.tryBegin())
            XCTAssertFalse(limiter.tryBegin(), "two pairings at once")
            limiter.end(.failed)
        }
        XCTAssertFalse(limiter.tryBegin())
        now = 299
        XCTAssertFalse(limiter.tryBegin())
        now = 300
        for _ in 0..<5 {
            XCTAssertTrue(limiter.tryBegin())
            limiter.end(.failed)
        }
        now = 899
        XCTAssertFalse(limiter.tryBegin())
        now = 900
        XCTAssertTrue(limiter.tryBegin())
        limiter.end(.paired)
    }

    func testAPairingClearsTheCount() {
        let limiter = PairingLimiter(now: { 0 })
        for _ in 0..<4 {
            XCTAssertTrue(limiter.tryBegin())
            limiter.end(.failed)
        }
        XCTAssertTrue(limiter.tryBegin())
        limiter.end(.paired)
        for _ in 0..<4 {
            XCTAssertTrue(limiter.tryBegin())
            limiter.end(.failed)
        }
        XCTAssertTrue(limiter.tryBegin())
    }
}
