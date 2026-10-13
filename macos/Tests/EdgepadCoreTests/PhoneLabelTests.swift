import XCTest

@testable import EdgepadCore

final class PhoneLabelTests: XCTestCase {
    private let address = "AA-BB-CC-DD-EE-FF"

    func testOrdinaryNameIsQuotedWithTheAddress() {
        let label = PhoneLabel.describe(name: "Akshit's Pixel", address: address)

        XCTAssertEqual(label, "\"Akshit's Pixel\" (\(address))")
    }

    func testMissingNameGivesTheAddressAlone() {
        let label = PhoneLabel.describe(name: nil, address: address)

        XCTAssertEqual(label, address)
    }

    func testNameOfOnlyStrippedCharactersGivesTheAddressAlone() {
        let label = PhoneLabel.describe(name: "\"!!\"\n😀", address: address)

        XCTAssertEqual(label, address)
    }

    func testNewlinesAndControlCharactersAreRemoved() {
        let label = PhoneLabel.describe(name: "Pixel\n\r\u{0007}Phone", address: address)

        XCTAssertEqual(label, "\"PixelPhone\" (\(address))")
    }

    func testLongNameIsCutToTheMaximumLength() {
        let name = String(repeating: "a", count: PhoneLabel.maxNameLength + 50)

        let label = PhoneLabel.describe(name: name, address: address)

        let expected = String(repeating: "a", count: PhoneLabel.maxNameLength)
        XCTAssertEqual(label, "\"\(expected)\" (\(address))")
    }

    func testCombiningMarksAreRemoved() {
        let name = "e" + String(repeating: "\u{0301}", count: 200)

        let label = PhoneLabel.describe(name: name, address: address)

        XCTAssertEqual(label, "\"e\" (\(address))")
    }

    func testVeryLongNameKeepsAtMostTheMaximumScalars() {
        let name = String(repeating: "ab ", count: 340)

        let label = PhoneLabel.describe(name: name, address: address)

        let shown = label.dropFirst().dropLast(address.count + 4)
        XCTAssertLessThanOrEqual(shown.unicodeScalars.count, PhoneLabel.maxNameLength)
    }

    func testQuotesInTheNameAreRemoved() {
        let label = PhoneLabel.describe(name: "My \"Pixel\"", address: address)

        XCTAssertEqual(label, "\"My Pixel\" (\(address))")
    }

    func testInnerRunsOfSpacesCollapse() {
        let label = PhoneLabel.describe(name: "  My    Pixel  ", address: address)

        XCTAssertEqual(label, "\"My Pixel\" (\(address))")
    }
}
