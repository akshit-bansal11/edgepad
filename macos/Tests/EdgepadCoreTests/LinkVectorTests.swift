import CryptoKit
import XCTest

@testable import EdgepadCore

/// protocol/link-vectors.txt, written by the C# core from the code the Windows and Linux laptops run: the Mac must
/// derive every value on it byte for byte, or a phone that pairs with one laptop could not pair with this one.
final class LinkVectorTests: XCTestCase {
    private typealias Line = (name: String, fields: [String: String])

    private func lines() throws -> [Line] {
        try Fixtures.lines("link-vectors.txt").map { line in
            let parts = line.split(separator: " ")
            var fields: [String: String] = [:]
            for part in parts.dropFirst() {
                let pair = part.split(separator: "=", maxSplits: 1)
                fields[String(pair[0])] = pair.count == 2 ? String(pair[1]) : ""
            }
            return (String(parts[0]), fields)
        }
    }

    private func line(_ name: String, in all: [Line]) throws -> [String: String] {
        try XCTUnwrap(all.first { $0.name == name }?.fields, "no \(name) line")
    }

    private func bytes(_ fields: [String: String], _ key: String) throws -> [UInt8] {
        try XCTUnwrap(Hex.bytes(try XCTUnwrap(fields[key], "no \(key)")), "\(key) is not hex")
    }

    func testEveryPairingValueIsReproduced() throws {
        let all = try lines()
        let input = try line("PAIR_INPUT", in: all)
        let pair = try line("PAIR", in: all)
        let keys = try line("PAIR_KEYS", in: all)
        let phone = try P256.KeyAgreement.PrivateKey(rawRepresentation: bytes(input, "dP"))
        let laptop = try P256.KeyAgreement.PrivateKey(rawRepresentation: bytes(input, "dL"))
        let pkP = LinkCrypto.publicKey(phone)
        let pkL = LinkCrypto.publicKey(laptop)
        XCTAssertEqual(pkP, try bytes(input, "pkP"))
        XCTAssertEqual(pkL, try bytes(input, "pkL"))
        XCTAssertEqual(LinkWire.nameField("Phone"), try bytes(input, "nameP"))
        XCTAssertEqual(LinkWire.nameField("Laptop"), try bytes(input, "nameL"))

        let nP = try bytes(input, "nP")
        let nL = try bytes(input, "nL")
        XCTAssertEqual(LinkCrypto.commit(pkP, nP), try bytes(pair, "commit"))
        let z = try LinkCrypto.agree(phone, pkL)
        XCTAssertEqual(z, try bytes(pair, "z"))
        XCTAssertEqual(try LinkCrypto.agree(laptop, pkP), z)
        let th = LinkCrypto.pairTranscript(
            phoneId: try bytes(input, "phoneId"), laptopId: try bytes(input, "laptopId"),
            phoneName: try bytes(input, "nameP"), laptopName: try bytes(input, "nameL"), phonePublic: pkP,
            phoneNonce: nP, laptopPublic: pkL, laptopNonce: nL)
        XCTAssertEqual(th, try bytes(pair, "th"))
        XCTAssertEqual(LinkCrypto.sas(th), pair["sas"])

        let prk = LinkCrypto.pairPrk(th, z)
        XCTAssertEqual(prk, try bytes(keys, "prk"))
        XCTAssertEqual(LinkCrypto.laptopTag(prk, th, pairing: true), try bytes(keys, "tagL"))
        XCTAssertEqual(LinkCrypto.phoneTag(prk, th, pairing: true), try bytes(keys, "tagP"))
        XCTAssertEqual(LinkCrypto.pairingKey(prk), try bytes(keys, "pk"))
        try checkSession("PAIR", prk: prk, in: all)
    }

    func testEveryResumeValueIsReproduced() throws {
        let all = try lines()
        let input = try line("PAIR_INPUT", in: all)
        let resumeInput = try line("RESUME_INPUT", in: all)
        let resume = try line("RESUME", in: all)
        let phone = try P256.KeyAgreement.PrivateKey(rawRepresentation: bytes(input, "dP"))
        let laptop = try P256.KeyAgreement.PrivateKey(rawRepresentation: bytes(input, "dL"))
        let th = LinkCrypto.resumeTranscript(
            phoneId: try bytes(input, "phoneId"), laptopId: try bytes(input, "laptopId"),
            phonePublic: LinkCrypto.publicKey(phone), phoneNonce: try bytes(resumeInput, "nP"),
            laptopPublic: LinkCrypto.publicKey(laptop), laptopNonce: try bytes(resumeInput, "nL"))
        XCTAssertEqual(th, try bytes(resume, "th"))

        let shared = try LinkCrypto.agree(laptop, LinkCrypto.publicKey(phone))
        let prk = LinkCrypto.resumePrk(th, shared, try bytes(resumeInput, "pk"))
        XCTAssertEqual(prk, try bytes(resume, "prk"))
        XCTAssertEqual(LinkCrypto.laptopTag(prk, th, pairing: false), try bytes(resume, "tagL"))
        XCTAssertEqual(LinkCrypto.phoneTag(prk, th, pairing: false), try bytes(resume, "tagP"))
        try checkSession("RESUME", prk: prk, in: all)
    }

    /// The direction keys, then the phone's two sealed records, and the laptop opening them.
    private func checkSession(_ name: String, prk: [UInt8], in all: [Line]) throws {
        let session = try line(name + "_SESSION", in: all)
        let keys = LinkCrypto.sessionKeys(prk)
        XCTAssertEqual(keys.phoneToLaptop, try bytes(session, "kP2L"))
        XCTAssertEqual(keys.laptopToPhone, try bytes(session, "kL2P"))
        XCTAssertEqual(keys.iv, try bytes(session, "iv"))

        let records = all.filter { $0.name == name + "_RECORD" }.map(\.fields)
        XCTAssertEqual(records.map { $0["counter"] }, ["0", "1"])
        let wire = FakeTransport()
        let phone = SecureTransport(inner: wire, prk: prk, isPhone: true)
        for record in records {
            XCTAssertTrue(phone.send(try bytes(record, "plain")))
        }
        XCTAssertEqual(wire.sent, try records.map { try bytes($0, "record") })

        let laptop = SecureTransport(inner: FakeTransport(), prk: prk)
        XCTAssertEqual(laptop.open(wire.sent.flatMap { $0 }), try records.flatMap { try bytes($0, "plain") })
    }

    func testAPointOffTheCurveIsRefused() throws {
        let own = LinkCrypto.newKeyPair()
        var bad = LinkCrypto.publicKey(LinkCrypto.newKeyPair())
        bad[64] ^= 0x01

        XCTAssertThrowsError(try LinkCrypto.agree(own, bad))
        XCTAssertThrowsError(try LinkCrypto.agree(own, [0x04] + [UInt8](repeating: 0, count: 64)))
        XCTAssertThrowsError(try LinkCrypto.agree(own, [0x02] + Array(LinkCrypto.publicKey(own).dropFirst())))
        XCTAssertThrowsError(try LinkCrypto.agree(own, Array(LinkCrypto.publicKey(own).dropLast())))
    }

    func testTheCodeIsComparedOnlyWhenItIsSixDigits() {
        XCTAssertTrue(LinkCrypto.codeMatches("009019", "009019"))
        XCTAssertFalse(LinkCrypto.codeMatches("009018", "009019"))
        XCTAssertFalse(LinkCrypto.codeMatches("9019", "009019"))
        XCTAssertFalse(LinkCrypto.codeMatches("00901９", "009019"))
        XCTAssertFalse(LinkCrypto.fixedTimeEquals([1, 2], [1, 2, 3]))
    }
}
