import XCTest

@testable import EdgepadCore

final class SignatureFileTests: XCTestCase {
    private let folder = FileManager.default.temporaryDirectory
        .appendingPathComponent("edgepad-signature-\(UUID().uuidString)")

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: folder)
    }

    func testASecondSaveWithTheSameStemGetsANumberAndNeverOverwritesTheFirst() throws {
        let first = try SignatureFile.writeNew(folder: folder, stem: "signature-1", png: Data([1]))
        let second = try SignatureFile.writeNew(folder: folder, stem: "signature-1", png: Data([2]))
        let third = try SignatureFile.writeNew(folder: folder, stem: "signature-1", png: Data([3]))

        let names = [first, second, third].map { $0.lastPathComponent }
        XCTAssertEqual(names, ["signature-1.png", "signature-1-2.png", "signature-1-3.png"])
        XCTAssertEqual(try Data(contentsOf: first), Data([1]))
        XCTAssertEqual(try Data(contentsOf: second), Data([2]))
    }
}
