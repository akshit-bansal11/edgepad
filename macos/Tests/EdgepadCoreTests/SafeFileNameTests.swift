import XCTest

@testable import EdgepadCore

/// The sanitiser against protocol/filenames.txt, which the Kotlin, C# and Swift suites all read, so they cannot drift.
final class SafeFileNameTests: XCTestCase {
    private static let rejected = "(rejected)"

    func testEveryPairInTheSharedFixture() throws {
        let lines = try Fixtures.lines("filenames.txt")
        XCTAssertFalse(lines.isEmpty)
        for line in lines {
            // Split at the LAST " | ". A line that starts with the bar had an empty input, trimmed by the reader.
            let input: String
            let expected: String
            if let bar = line.range(of: " | ", options: .backwards) {
                input = String(line[..<bar.lowerBound])
                expected = String(line[bar.upperBound...])
            } else {
                XCTAssertTrue(line.hasPrefix("| "), "Not a pair: \(line)")
                input = ""
                expected = String(line.dropFirst(2))
            }
            let want = expected == Self.rejected ? nil : expected
            XCTAssertEqual(SafeFileName.clean(input), want, "Input: \(input)")
        }
    }

    func testControlCharactersAreDroppedAndAllControlNamesRefused() {
        XCTAssertEqual(SafeFileName.clean("a\u{7}b\u{0}.txt"), "ab.txt")
        XCTAssertEqual(SafeFileName.clean("\u{1}\u{2}"), nil)
        XCTAssertNil(SafeFileName.clean(nil))
    }

    func testALongNameKeepsItsExtensionAndStaysWithinTheByteLimit() throws {
        let cleaned = try XCTUnwrap(SafeFileName.clean(String(repeating: "é", count: 200) + ".pdf"))
        XCTAssertLessThanOrEqual(cleaned.utf8.count, TransferLimits.maxNameBytes)
        XCTAssertTrue(cleaned.hasSuffix(".pdf"))
    }

    func testAClashIsRenamedNeverOverwritten() {
        let taken: Set<String> = ["a.txt", "a (1).txt", "noext", "archive.tar.gz"]
        XCTAssertEqual(SafeFileName.unique("b.txt") { taken.contains($0) }, "b.txt")
        XCTAssertEqual(SafeFileName.unique("a.txt") { taken.contains($0) }, "a (2).txt")
        XCTAssertEqual(SafeFileName.unique("noext") { taken.contains($0) }, "noext (1)")
        XCTAssertEqual(SafeFileName.unique("archive.tar.gz") { taken.contains($0) }, "archive.tar (1).gz")
    }

    func testTheQuarantineValueHasTheDownloadForm() {
        XCTAssertEqual(Quarantine.value(at: Date(timeIntervalSince1970: 0x5e6a_7b2c)), "0081;5e6a7b2c;Edgepad;")
        XCTAssertEqual(Quarantine.value(at: Date(timeIntervalSince1970: -5)), "0081;0;Edgepad;")
    }
}
