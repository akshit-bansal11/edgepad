import XCTest

@testable import EdgepadCore

/// Holds the id enums to protocol/actions.txt, which the other apps read too.
final class IdsFixtureTests: XCTestCase {
    func testActionIdsMatchTheSharedTable() throws {
        XCTAssertEqual(try expected("ACTION"), actual(ActionId.self))
    }

    func testControlIdsMatchTheSharedTable() throws {
        XCTAssertEqual(try expected("CONTROL"), actual(ControlId.self))
    }

    func testTextKindsMatchTheSharedTable() throws {
        XCTAssertEqual(try expected("TEXT"), actual(TextKind.self))
    }

    // PAD_BUTTON rows are not checked: a Mac has no virtual controller, so it drops PAD_STATE and never reads those
    // values. The kind is still recognised below, so an unknown kind in actions.txt still fails.
    func testEveryRowKindInTheSharedTableIsKnown() throws {
        let known: Set<String> = [
            "ACTION",
            "CONTROL",
            "TEXT",
            "PAD_BUTTON",
            "PAD_STATUS",
            "FEATURE",
            "SIGN_STATUS",
            "PALETTE",
            "CAPTURE_STATUS",
            "FILE_TYPE",
            "FILE_LIMIT",
            "FILE_STATUS",
            "HANDSHAKE",
        ]
        let kinds = try Fixtures.lines("actions.txt")
            .map { $0.split(separator: " ").map(String.init) }
            .filter { $0.count >= 3 }
            .map { $0[0] }

        XCTAssertEqual(Set(kinds).subtracting(known), [])
    }

    func testPadStatusTokensMatchTheSharedTable() throws {
        // The token is what crosses the wire, so the row's second column is the raw value and the third the name.
        var table: [String: String] = [:]
        for parts in try rows("PAD_STATUS") {
            table[parts[1]] = normalised(parts[2])
        }

        let enumTable = Dictionary(
            uniqueKeysWithValues: PadStatus.allCases.map { ($0.rawValue, normalised(String(describing: $0))) })

        XCTAssertEqual(table, enumTable)
    }

    func testFeatureTokensMatchTheSharedTable() throws {
        var table: [String: String] = [:]
        for parts in try rows("FEATURE") {
            table[parts[1]] = normalised(parts[2])
        }

        let enumTable = Dictionary(
            uniqueKeysWithValues: Feature.allCases.map { ($0.rawValue, normalised(String(describing: $0))) })

        XCTAssertEqual(table, enumTable)
    }

    func testSignStatusTokensMatchTheSharedTable() throws {
        var table: [String: String] = [:]
        for parts in try rows("SIGN_STATUS") {
            table[parts[1]] = normalised(parts[2])
        }

        let enumTable = Dictionary(
            uniqueKeysWithValues: SignStatus.allCases.map { ($0.rawValue, normalised(String(describing: $0))) })

        XCTAssertEqual(table, enumTable)
    }

    func testCaptureStatusTokensMatchTheSharedTable() throws {
        var table: [String: String] = [:]
        for parts in try rows("CAPTURE_STATUS") {
            table[parts[1]] = normalised(parts[2])
        }

        let enumTable = Dictionary(
            uniqueKeysWithValues: CaptureStatus.allCases.map { ($0.rawValue, normalised(String(describing: $0))) })

        XCTAssertEqual(table, enumTable)
    }

    func testFileTypesMatchTheSharedTable() throws {
        XCTAssertEqual(try expected("FILE_TYPE"), actual(FileType.self))
        for parts in try rows("FILE_TYPE") {
            let type = try XCTUnwrap(FileType(rawValue: UInt8(parts[1]) ?? 255))
            XCTAssertEqual(type.mime, parts[3])
            XCTAssertEqual(type.fileExtension, parts[4])
            XCTAssertEqual(FileType(path: "/tmp/Screen Shot 1." + parts[4].uppercased()), type)
        }
        XCTAssertNil(FileType(path: "/tmp/notes.txt"))
        XCTAssertNil(FileType(path: "/tmp/png"))
    }

    func testFileLimitsMatchTheSharedTable() throws {
        var limits: [String: Int] = [:]
        for parts in try rows("FILE_LIMIT") {
            limits[parts[2]] = Int(parts[1])
        }

        XCTAssertEqual(limits, ["IMAGE": FileType.imageLimit, "VIDEO": FileType.videoLimit])
    }

    func testFileStatusesMatchTheSharedTable() throws {
        XCTAssertEqual(try expected("FILE_STATUS"), actual(FileStatus.self))
    }

    func testThePaletteMatchesTheSharedTable() throws {
        let palette = try rows("PALETTE")

        XCTAssertEqual(palette.count, Palette.count)
        for parts in palette {
            XCTAssertEqual(Palette.colour(Int(parts[1]) ?? -1), UInt32(parts[3], radix: 16), parts[2])
        }
    }

    func testTheHandshakeMatchesTheSharedTable() throws {
        var handshake: [String: String] = [:]
        for parts in try rows("HANDSHAKE") {
            handshake[parts[2]] = parts[1]
        }

        XCTAssertEqual(handshake["VERSION"], String(ProtocolConstants.version))
        XCTAssertEqual(handshake["SERVICE_ID"], ProtocolConstants.serviceId)
    }

    private func rows(_ kind: String) throws -> [[String]] {
        let found = try Fixtures.lines("actions.txt")
            .map { $0.split(separator: " ").map(String.init) }
            .filter { $0.count >= 3 && $0[0] == kind }
        XCTAssertFalse(found.isEmpty, kind)
        return found
    }

    private func expected(_ kind: String) throws -> [Int: String] {
        var table: [Int: String] = [:]
        for parts in try rows(kind) {
            table[Int(parts[1]) ?? -1] = normalised(parts[2])
        }
        return table
    }

    private func actual<T: CaseIterable & RawRepresentable>(_ type: T.Type) -> [Int: String]
    where T.RawValue: BinaryInteger {
        Dictionary(
            uniqueKeysWithValues: T.allCases.map { (Int($0.rawValue), normalised(String(describing: $0))) })
    }

    /// MUTE_TOGGLE and muteToggle meet at "mutetoggle": the file's spelling and Swift's, compared without either.
    private func normalised(_ name: String) -> String {
        name.replacingOccurrences(of: "_", with: "").lowercased()
    }
}
