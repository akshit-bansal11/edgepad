import XCTest

@testable import EdgepadCore

final class SecureTransportTests: XCTestCase {
    private let prk = Hex.random(32)

    func testA40KilobyteWriteIsSplitIntoRecordsAndReassembledFromAnyCut() throws {
        let wire = FakeTransport()
        let phone = SecureTransport(inner: wire, prk: prk, isPhone: true)
        let plain = (0..<40_000).map { UInt8(truncatingIfNeeded: $0) }

        XCTAssertTrue(phone.send(plain))
        let bytes = wire.sent.flatMap { $0 }
        XCTAssertEqual(bytes.count, plain.count + 3 * 18)

        // Delivered a byte at a time: nothing is given back until a whole record is in.
        let laptop = SecureTransport(inner: FakeTransport(), prk: prk)
        var opened: [UInt8] = []
        for byte in bytes {
            opened += try XCTUnwrap(laptop.open([byte]))
        }
        XCTAssertEqual(opened, plain)
    }

    func testTheLaptopAnswersInItsOwnDirection() {
        let wire = FakeTransport()
        XCTAssertTrue(SecureTransport(inner: wire, prk: prk).send([1, 2, 3]))

        XCTAssertNil(SecureTransport(inner: FakeTransport(), prk: prk).open(wire.sent[0]))
        XCTAssertEqual(SecureTransport(inner: FakeTransport(), prk: prk, isPhone: true).open(wire.sent[0]), [1, 2, 3])
    }

    func testATamperedRecordClosesForGood() {
        let wire = FakeTransport()
        let phone = SecureTransport(inner: wire, prk: prk, isPhone: true)
        XCTAssertTrue(phone.send([1, 2, 3]))
        XCTAssertTrue(phone.send([4, 5, 6]))
        var tampered = wire.sent[0]
        tampered[3] ^= 1

        let laptop = SecureTransport(inner: FakeTransport(), prk: prk)
        XCTAssertNil(laptop.open(tampered))
        XCTAssertNil(laptop.open(wire.sent[1]), "a link that failed once opened another record")
        XCTAssertTrue(laptop.failed)
    }

    func testAReplayedOrReorderedRecordFails() {
        let wire = FakeTransport()
        let phone = SecureTransport(inner: wire, prk: prk, isPhone: true)
        XCTAssertTrue(phone.send([1]))
        XCTAssertTrue(phone.send([2]))

        let replayed = SecureTransport(inner: FakeTransport(), prk: prk)
        XCTAssertEqual(replayed.open(wire.sent[0]), [1])
        XCTAssertNil(replayed.open(wire.sent[0]))

        let reordered = SecureTransport(inner: FakeTransport(), prk: prk)
        XCTAssertNil(reordered.open(wire.sent[1]))
    }

    func testALengthOutOfRangeFails() {
        XCTAssertNil(SecureTransport(inner: FakeTransport(), prk: prk).open([16, 0]))
        XCTAssertNil(SecureTransport(inner: FakeTransport(), prk: prk).open([0xFF, 0xFF]))
    }

    func testAWriteTheConnectionRefusesFailsTheLinkForGood() {
        let wire = FakeTransport()
        let laptop = SecureTransport(inner: wire, prk: prk)
        wire.alive = false
        XCTAssertFalse(laptop.send([1]))

        wire.alive = true
        XCTAssertFalse(laptop.send([2]), "a record went out after one was lost")
        XCTAssertTrue(wire.sent.isEmpty)
    }
}

final class LinkStorageTests: XCTestCase {
    private let directory = URL(fileURLWithPath: NSTemporaryDirectory())
        .appendingPathComponent("edgepad-links-" + UUID().uuidString)

    private func path(_ name: String) -> String {
        directory.appendingPathComponent(name).path
    }

    private func write(_ text: String, to name: String) throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try text.write(toFile: path(name), atomically: true, encoding: .utf8)
    }

    override func tearDown() {
        try? FileManager.default.removeItem(at: directory)
        super.tearDown()
    }

    func testAPairingIsKeptAndForgotten() {
        let links = PairedLinks(path: path("paired-links.txt"))
        let id = String(repeating: "ab", count: 16)
        let key = Hex.random(32)
        XCTAssertFalse(links.canForget)

        XCTAssertTrue(links.add(id, key: key, name: "Pixel\tline\nbreak", since: links.generation))
        let found = PairedLinks(path: path("paired-links.txt")).find(id.uppercased())
        XCTAssertEqual(found?.key, key)
        XCTAssertEqual(found?.name, "Pixel line break")
        XCTAssertTrue(links.canForget)

        XCTAssertTrue(links.forget())
        XCTAssertNil(links.find(id))
        XCTAssertFalse(links.canForget)
    }

    func testAFileEdgepadDidNotWriteRefusesEveryPhone() throws {
        let id = String(repeating: "ab", count: 16)
        try write("\(id)\tnot base64\tPixel\n", to: "paired-links.txt")
        let links = PairedLinks(path: path("paired-links.txt"))

        XCTAssertNil(links.find(id))
        XCTAssertFalse(
            links.add(id, key: Hex.random(32), name: "Pixel", since: links.generation),
            "wrote over phones it could not read")
        XCTAssertTrue(links.canForget)
        XCTAssertTrue(links.forget())
        XCTAssertTrue(links.add(id, key: Hex.random(32), name: "Pixel", since: links.generation))
    }

    func testAFileThatCannotBeReadRefusesEveryPhone() throws {
        // A folder where the file should be: there, and not readable as a file.
        try FileManager.default.createDirectory(
            atPath: path("paired-links.txt"), withIntermediateDirectories: true)

        XCTAssertNil(PairedLinks(path: path("paired-links.txt")).find(String(repeating: "ab", count: 16)))
        XCTAssertTrue(PairedLinks(path: path("paired-links.txt")).canForget)
    }

    func testAGrantedPhoneIsAdmittedWithoutAsking() {
        let trust = TrustStore(path: path("trusted-phone.txt"))
        trust.grant("link:00", since: trust.forgetGeneration)
        trust.grant("link:00", since: trust.forgetGeneration)

        var asked = false
        XCTAssertTrue(
            trust.admit("link:00") {
                asked = true
                return false
            })
        XCTAssertFalse(asked, "asked about a granted phone")
        XCTAssertEqual(TrustStore(path: path("trusted-phone.txt")).trustedPhones, ["link:00"])
    }

    func testAGrantDoesNotWriteOverATrustFileThatCannotBeRead() throws {
        try FileManager.default.createDirectory(
            atPath: path("trusted-phone.txt"), withIntermediateDirectories: true)
        let trust = TrustStore(path: path("trusted-phone.txt"))
        trust.grant("link:00", since: trust.forgetGeneration)

        XCTAssertFalse(trust.admit("link:00") { true })
    }

    func testNetworkSettingsAreOffUntilChosenAndFailClosed() throws {
        let settings = NetworkSettings(path: path("network.txt"))
        XCTAssertFalse(settings.load().enabled)
        XCTAssertFalse(settings.hasChoice)
        XCTAssertEqual(settings.load().port, NetworkSettings.defaultPort)

        XCTAssertTrue(settings.setEnabled(false))
        XCTAssertFalse(settings.load().enabled)
        XCTAssertTrue(settings.hasChoice)

        XCTAssertTrue(settings.setEnabled(true))
        XCTAssertTrue(settings.load().enabled)
        XCTAssertEqual(try String(contentsOfFile: path("network.txt"), encoding: .utf8), "enabled=1\nport=47321\n")

        try write("enabled=1\nport=5000\n", to: "network.txt")
        XCTAssertEqual(settings.load().port, 5000)
        for text in ["enabled=yes\n", "enabled=1\nport=+5\n", "enabled=1\nport=70000\n", "enabled=1\nother=1\n"] {
            try write(text, to: "network.txt")
            XCTAssertFalse(settings.load().enabled, text)
            XCTAssertTrue(settings.hasChoice, text)
        }

        // A folder where the file should be: there, and not readable.
        try FileManager.default.createDirectory(atPath: path("folder.txt"), withIntermediateDirectories: true)
        let unreadable = NetworkSettings(path: path("folder.txt"))
        XCTAssertFalse(unreadable.load().enabled)
        XCTAssertTrue(unreadable.hasChoice)

        // Neither a folder that is not there nor a link to nothing reads as on.
        XCTAssertFalse(NetworkSettings(path: path("gone/network.txt")).load().enabled)
        try FileManager.default.createSymbolicLink(
            atPath: path("dangling.txt"), withDestinationPath: path("nothing.txt"))
        XCTAssertFalse(NetworkSettings(path: path("dangling.txt")).load().enabled)
    }

    func testTheLinkIdIsMadeOnceAndKept() {
        let first = LinkIdentity.load(path: path("link-id.txt"), name: "Mac")
        let again = LinkIdentity.load(path: path("link-id.txt"), name: "Mac")

        XCTAssertEqual(first.id.count, 16)
        XCTAssertEqual(again.id, first.id)
        XCTAssertEqual(first.idHex.count, 32)
    }

    func testANetworkPhonesNameIsCleanedForTheStatusLine() {
        XCTAssertEqual(PhoneLabel.clean("Pixel 8 <Pro>"), "Pixel 8 Pro")
        XCTAssertEqual(PhoneLabel.clean(nil), "")
        XCTAssertEqual(PhoneLabel.describe(name: "Pixel", address: "AA"), "\"Pixel\" (AA)")
    }
}
