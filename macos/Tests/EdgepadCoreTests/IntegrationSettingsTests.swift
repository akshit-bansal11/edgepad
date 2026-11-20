import XCTest

@testable import EdgepadCore

final class IntegrationSettingsTests: XCTestCase {
    private static let flags = [
        IntegrationSettings.stats, IntegrationSettings.weather, IntegrationSettings.claude, IntegrationSettings.codex,
        IntegrationSettings.gitHub, IntegrationSettings.gitHubPrivate, IntegrationSettings.codexTrusted,
    ]

    private var dir = ""

    override func setUpWithError() throws {
        dir = (NSTemporaryDirectory() as NSString).appendingPathComponent("edgepad-integrations-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(atPath: dir)
    }

    private var filePath: String { (dir as NSString).appendingPathComponent(IntegrationSettings.fileName) }

    private func assertAllOff(_ settings: IntegrationSettings, _ note: String = "") {
        for key in Self.flags {
            XCTAssertFalse(settings.flag(key), "\(key) \(note)")
        }
        XCTAssertEqual(settings.text(IntegrationSettings.city), "", note)
        XCTAssertEqual(settings.text(IntegrationSettings.gitHubLogin), "", note)
    }

    func testANewInstallHasEverythingOff() {
        assertAllOff(IntegrationSettings(path: filePath))
    }

    func testGarbageIsAllOff() throws {
        let contents = [
            "\0\0\0zzz", "just some words\nand more words", "stats\ngithub\n", "=1\n==\n=stats=1",
            "stats=yes\nweather=true\nclaude=2\ncodex= 0\ngithubPrivate=maybe\n", "bad key=1\nstats!=1\n", "",
        ]
        for content in contents {
            try content.write(toFile: filePath, atomically: true, encoding: .utf8)
            assertAllOff(IntegrationSettings(path: filePath), content)
        }
    }

    func testAFileThatIsTooLargeIsAllOff() throws {
        try ("stats=1\n" + String(repeating: "#", count: 70 * 1024)).write(
            toFile: filePath, atomically: true, encoding: .utf8)
        assertAllOff(IntegrationSettings(path: filePath))
    }

    func testAnUnreadableFileIsAllOff() throws {
        try FileManager.default.createDirectory(atPath: filePath, withIntermediateDirectories: true)
        assertAllOff(IntegrationSettings(path: filePath))
    }

    func testOnlyTheValueOneIsOn() throws {
        try "stats=1\nweather=0\nclaude=11\ncodex=1 \n".write(toFile: filePath, atomically: true, encoding: .utf8)

        let settings = IntegrationSettings(path: filePath)

        XCTAssertTrue(settings.flag(IntegrationSettings.stats))
        XCTAssertFalse(settings.flag(IntegrationSettings.weather))
        XCTAssertFalse(settings.flag(IntegrationSettings.claude))
        XCTAssertTrue(settings.flag(IntegrationSettings.codex))
    }

    func testEverythingRoundTrips() {
        let settings = IntegrationSettings(path: filePath)
        XCTAssertTrue(settings.setFlag(IntegrationSettings.stats, true))
        XCTAssertTrue(settings.setFlag(IntegrationSettings.gitHub, true))
        XCTAssertTrue(settings.setFlag(IntegrationSettings.gitHubPrivate, true))
        XCTAssertTrue(settings.setFlag(IntegrationSettings.codexTrusted, true))
        XCTAssertTrue(settings.setFlag(IntegrationSettings.weather, false))
        let texts = [
            (IntegrationSettings.city, "Lisbon"), (IntegrationSettings.lat, "38.72"),
            (IntegrationSettings.lon, "-9.14"),
            (IntegrationSettings.place, "Lisbon, Portugal"), (IntegrationSettings.gitHubLogin, "octo-cat"),
            (IntegrationSettings.pluginVersionKey("claude"), "5.0.0"),
            (IntegrationSettings.pluginNodeKey("codex"), "/opt/homebrew/bin/node"),
        ]
        for (key, value) in texts {
            XCTAssertTrue(settings.set(key, value))
        }

        let again = IntegrationSettings(path: filePath)

        XCTAssertTrue(again.flag(IntegrationSettings.stats))
        XCTAssertTrue(again.flag(IntegrationSettings.gitHub))
        XCTAssertTrue(again.flag(IntegrationSettings.gitHubPrivate))
        XCTAssertTrue(again.flag(IntegrationSettings.codexTrusted))
        XCTAssertFalse(again.flag(IntegrationSettings.weather))
        XCTAssertFalse(again.flag(IntegrationSettings.claude))
        for (key, value) in texts {
            XCTAssertEqual(again.text(key), value, key)
        }
    }

    func testAnEqualsSignAndUnicodeSurviveInTheCity() {
        for city in ["A=B=C", "São Paulo", "東京", "Zürich 🌧"] {
            IntegrationSettings(path: filePath).set(IntegrationSettings.city, city)
            XCTAssertEqual(IntegrationSettings(path: filePath).text(IntegrationSettings.city), city)
        }
    }

    func testAValueIsOneLineWithoutControlCharacters() throws {
        let settings = IntegrationSettings(path: filePath)

        settings.set(IntegrationSettings.city, "  Paris\nstats=1\r\n\tFrance\0  ")

        XCTAssertEqual(settings.text(IntegrationSettings.city), "Paris stats=1   France")
        XCTAssertFalse(IntegrationSettings(path: filePath).flag(IntegrationSettings.stats))
        let text = try String(contentsOfFile: filePath, encoding: .utf8)
        XCTAssertEqual(text.unicodeScalars.filter { $0 == "\n" }.count, 1)
    }

    func testTheCityIsCutAtSixtyCodePoints() {
        let settings = IntegrationSettings(path: filePath)

        settings.set(IntegrationSettings.city, String(repeating: "a", count: 59) + "😀😀")

        XCTAssertEqual(settings.text(IntegrationSettings.city), String(repeating: "a", count: 59) + "😀")
        XCTAssertEqual(settings.text(IntegrationSettings.city).unicodeScalars.count, 60)
    }

    func testAnEmptyValueRemovesTheKey() throws {
        let settings = IntegrationSettings(path: filePath)
        settings.set(IntegrationSettings.city, "Rome")

        settings.set(IntegrationSettings.city, "   ")

        XCTAssertEqual(IntegrationSettings(path: filePath).text(IntegrationSettings.city), "")
        XCTAssertFalse(try String(contentsOfFile: filePath, encoding: .utf8).contains("city"))
    }

    func testKeysFromANewerBuildAreKept() throws {
        try "future.thing=hello wörld\nstats=1\n".write(toFile: filePath, atomically: true, encoding: .utf8)
        let settings = IntegrationSettings(path: filePath)

        settings.set(IntegrationSettings.city, "Oslo")

        let text = try String(contentsOfFile: filePath, encoding: .utf8)
        XCTAssertTrue(text.contains("future.thing=hello wörld\n"))
        XCTAssertTrue(text.contains("stats=1\n"))
        XCTAssertEqual(IntegrationSettings(path: filePath).text("future.thing"), "hello wörld")
    }

    func testABadKeyIsRefusedAndNothingIsWritten() {
        let settings = IntegrationSettings(path: filePath)
        for key in ["", "a b", "a=b", "a\nb"] {
            XCTAssertFalse(settings.set(key, "1"))
        }
        XCTAssertFalse(FileManager.default.fileExists(atPath: filePath))
    }

    func testAFailedWriteReturnsFalseButTheChangeTakesEffect() throws {
        let blocker = (dir as NSString).appendingPathComponent("blocker")
        try "a file where a folder should be".write(toFile: blocker, atomically: true, encoding: .utf8)
        let inside = (blocker as NSString).appendingPathComponent(IntegrationSettings.fileName)
        let settings = IntegrationSettings(path: inside)

        XCTAssertFalse(settings.setFlag(IntegrationSettings.stats, true))
        XCTAssertTrue(settings.flag(IntegrationSettings.stats))
    }

    func testFourThreadsNeverCorruptTheFile() {
        let settings = IntegrationSettings(path: filePath)
        DispatchQueue.concurrentPerform(iterations: 4) { t in
            for i in 0..<50 {
                settings.setFlag(Self.flags[t], i % 2 == 0)
            }
        }
        let again = IntegrationSettings(path: filePath)
        for t in 0..<4 {
            XCTAssertEqual(again.flag(Self.flags[t]), settings.flag(Self.flags[t]))
        }
    }

    func testAgentsAndGitHubNeedDeveloperModeToBeActive() {
        let settings = IntegrationSettings(path: filePath)
        settings.setFlag(IntegrationSettings.claude, true)
        settings.setFlag(IntegrationSettings.codex, true)
        settings.setFlag(IntegrationSettings.gitHub, true)
        settings.setFlag(IntegrationSettings.stats, true)
        XCTAssertFalse(settings.active(IntegrationSettings.claude))
        XCTAssertFalse(settings.active(IntegrationSettings.codex))
        XCTAssertFalse(settings.active(IntegrationSettings.gitHub))
        XCTAssertTrue(settings.active(IntegrationSettings.stats))

        settings.setFlag(IntegrationSettings.developer, true)
        XCTAssertTrue(settings.active(IntegrationSettings.claude))
        XCTAssertTrue(settings.active(IntegrationSettings.codex))
        XCTAssertTrue(settings.active(IntegrationSettings.gitHub))

        settings.setFlag(IntegrationSettings.developer, false)
        XCTAssertTrue(settings.flag(IntegrationSettings.claude))
        XCTAssertFalse(settings.active(IntegrationSettings.claude))
    }
}
