import Foundation
import XCTest

@testable import EdgepadCore

/// The cases of the Windows `PinStoreTests`, against the Mac's store.
final class PinStoreTests: XCTestCase {
    private let phone = "AA:BB:CC:DD:EE:FF"
    private let other = "11:22:33:44:55:66"
    private let directory = FileManager.default.temporaryDirectory
        .appendingPathComponent("edgepad-pin-\(UUID().uuidString)")

    /// Seconds on the store's clock, moved by hand.
    private var time: TimeInterval = 0

    private var securityFile: String { directory.appendingPathComponent("security.txt").path }

    override func tearDownWithError() throws {
        if FileManager.default.fileExists(atPath: directory.path) {
            try FileManager.default.removeItem(at: directory)
        }
    }

    /// One iteration: the real work factor would take most of a second per hash, and the format is the same.
    private func newStore(iterations: UInt32 = 1) -> PinStore {
        PinStore(directory: directory.path, now: { [weak self] in self?.time ?? 0 }, iterations: iterations)
    }

    /// Only ever handed six digits written into the test, so the unwrap cannot fail.
    private func pin(_ text: String) -> Pin {
        Pin(digits: text.compactMap { $0.wholeNumberValue }.map { UInt8($0) })!
    }

    private func makeStore(level: Int) -> PinStore {
        let store = newStore()
        XCTAssertTrue(store.setPin(pin("123456")))
        XCTAssertTrue(store.setLevel(level))
        return store
    }

    private func readSecurityFile() throws -> String {
        try String(contentsOfFile: securityFile, encoding: .utf8)
    }

    func testNoFileIsLevelOneWithNoPin() {
        let store = newStore()

        XCTAssertEqual(store.level, 1)
        XCTAssertFalse(store.hasPin)
        XCTAssertFalse(store.required(phone))
    }

    func testTheHashVerifiesAndAWrongPinFails() throws {
        let store = makeStore(level: 3)

        let text = try readSecurityFile()
        XCTAssertNotNil(
            text.range(
                of: #"^level=3\npin=pbkdf2-sha256\$1\$[A-Za-z0-9+/=]{24}\$[A-Za-z0-9+/=]{44}\n$"#,
                options: .regularExpression))
        XCTAssertEqual(store.check(phone, pin("654321")), .wrong(attemptsLeft: 4))
        XCTAssertEqual(store.check(phone, pin("123456")), .accepted)

        // The count comes from the stored hash, not the store, so raising the constant breaks no saved PIN.
        XCTAssertEqual(newStore(iterations: 2).check(phone, pin("123456")), .accepted)
    }

    func testTwoSetPinCallsGiveADifferentSaltAndHash() throws {
        let store = newStore()
        XCTAssertTrue(store.setPin(pin("123456")))
        let first = try readSecurityFile().components(separatedBy: "$")
        XCTAssertTrue(store.setPin(pin("123456")))
        let second = try readSecurityFile().components(separatedBy: "$")

        XCTAssertNotEqual(first[2], second[2])
        XCTAssertNotEqual(first[3], second[3])
    }

    func testTheFilesArePrivateToTheUser() throws {
        let store = makeStore(level: 2)
        XCTAssertEqual(store.check(phone, pin("123456")), .accepted)

        let manager = FileManager.default
        let folder = try manager.attributesOfItem(atPath: directory.path)[.posixPermissions] as? NSNumber
        let security = try manager.attributesOfItem(atPath: securityFile)[.posixPermissions] as? NSNumber
        let verified = try manager.attributesOfItem(
            atPath: directory.appendingPathComponent("pin-verified.txt").path)[.posixPermissions] as? NSNumber
        XCTAssertEqual(folder?.intValue, 0o700)
        XCTAssertEqual(security?.intValue, 0o600)
        XCTAssertEqual(verified?.intValue, 0o600)
    }

    func testFiveFailuresLockFor30SecondsAndEachLaterBatchDoublesUpToAnHour() throws {
        let store = makeStore(level: 3)
        let wrong = pin("654321")
        for left in stride(from: 4, to: 0, by: -1) {
            XCTAssertEqual(store.check(phone, wrong), .wrong(attemptsLeft: left))
        }

        XCTAssertEqual(store.check(phone, wrong), .locked(seconds: 30))

        // Locked out means the answer is not looked at, the right one included; the key ignores case, and another
        // phone is not affected.
        XCTAssertEqual(store.check(phone, pin("123456")), .locked(seconds: 30))
        XCTAssertEqual(store.lockedFor("aa:bb:cc:dd:ee:ff"), 30)
        XCTAssertEqual(store.attemptsLeft(phone), 0)
        XCTAssertNil(store.lockedFor(other))

        for seconds in [60, 120, 240, 480, 960, 1920, 3600, 3600] {
            time += 3600
            XCTAssertEqual(store.attemptsLeft(phone), 5)
            for _ in 0..<4 {
                _ = store.check(phone, wrong)
            }
            XCTAssertEqual(store.check(phone, wrong), .locked(seconds: seconds))
        }
    }

    func testTheLockExpiresOnTheClock() throws {
        let store = makeStore(level: 3)
        for _ in 0..<5 {
            _ = store.check(phone, pin("654321"))
        }

        time += 29.5
        XCTAssertEqual(store.check(phone, pin("123456")), .locked(seconds: 1))

        time += 0.5
        XCTAssertNil(store.lockedFor(phone))
        XCTAssertEqual(store.check(phone, pin("123456")), .accepted)
    }

    func testASuccessResetsTheFailures() throws {
        let store = makeStore(level: 3)
        for _ in 0..<4 {
            _ = store.check(phone, pin("654321"))
        }

        XCTAssertEqual(store.check(phone, pin("123456")), .accepted)
        XCTAssertEqual(store.attemptsLeft(phone), 5)
        for _ in 0..<4 {
            _ = store.check(phone, pin("654321"))
        }

        XCTAssertNil(store.lockedFor(phone))
        XCTAssertEqual(store.attemptsLeft(phone), 1)
    }

    func testLevelTwoAsksOnceAndLevelThreeEveryTime() throws {
        let two = makeStore(level: 2)
        XCTAssertTrue(two.required(phone))
        XCTAssertEqual(two.check(phone, pin("123456")), .accepted)
        XCTAssertFalse(two.required("aa:bb:cc:dd:ee:ff"))
        XCTAssertFalse(newStore().required(phone))
        XCTAssertTrue(two.required(other))

        let three = makeStore(level: 3)
        XCTAssertEqual(three.check(phone, pin("123456")), .accepted)
        XCTAssertTrue(three.required(phone))
    }

    func testSetPinAndSetLevelClearTheVerifiedList() throws {
        let store = makeStore(level: 2)
        _ = store.check(phone, pin("123456"))
        XCTAssertTrue(store.setPin(pin("111111")))
        XCTAssertTrue(store.required(phone))

        _ = store.check(phone, pin("111111"))
        XCTAssertFalse(store.required(phone))
        XCTAssertTrue(store.setLevel(2))
        XCTAssertTrue(store.required(phone))
    }

    func testLevelsTwoAndThreeAreRefusedWithoutAPin() throws {
        let store = newStore()

        XCTAssertFalse(store.setLevel(2))
        XCTAssertFalse(store.setLevel(3))
        XCTAssertFalse(store.setLevel(4))
        XCTAssertEqual(store.level, 1)
        XCTAssertEqual(store.check(phone, pin("123456")), .unavailable)
    }

    func testAFileThatCannotBeReadFailsClosed() throws {
        // A folder where the file should be cannot be read as text by anyone, root included. A phone verified
        // before is asked again and cannot be answered: an unreadable PIN is never "no PIN".
        let store = makeStore(level: 2)
        _ = store.check(phone, pin("123456"))
        let saved = try readSecurityFile()
        try FileManager.default.removeItem(atPath: securityFile)
        try FileManager.default.createDirectory(atPath: securityFile, withIntermediateDirectories: false)

        XCTAssertTrue(store.required(phone))
        XCTAssertEqual(store.check(phone, pin("123456")), .unavailable)
        XCTAssertEqual(store.level, 3)
        XCTAssertTrue(store.hasPin)
        XCTAssertFalse(store.setPin(pin("111111")))
        XCTAssertFalse(store.setLevel(1))

        try FileManager.default.removeItem(atPath: securityFile)
        try saved.write(toFile: securityFile, atomically: true, encoding: .utf8)
        XCTAssertFalse(store.required(phone))
    }

    func testAFileEdgepadWouldNotWriteFailsClosed() throws {
        for text in ["level=2\n", "level=1\npin=nonsense\n", "level=9\n", "level=+1\n", "garbled"] {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            try text.write(toFile: securityFile, atomically: true, encoding: .utf8)
            let store = newStore()

            XCTAssertTrue(store.required(phone), text)
            XCTAssertEqual(store.check(phone, pin("123456")), .unavailable, text)
            XCTAssertTrue(store.reset(), text)
            XCTAssertFalse(store.required(phone), text)
        }
    }

    func testResetForgetsThePinAndTheVerifiedPhones() throws {
        let store = makeStore(level: 2)
        _ = store.check(phone, pin("123456"))

        XCTAssertTrue(store.reset())
        XCTAssertEqual(store.level, 1)
        XCTAssertFalse(store.hasPin)
        XCTAssertTrue(store.setPin(pin("123456")))
        XCTAssertTrue(store.setLevel(2))
        XCTAssertTrue(store.required(phone))
    }

    func testTheDigitsAppearInNoFile() throws {
        let store = makeStore(level: 2)
        _ = store.check(phone, pin("123456"))

        XCTAssertFalse(try readSecurityFile().contains("123456"))
        let verified = try String(
            contentsOfFile: directory.appendingPathComponent("pin-verified.txt").path, encoding: .utf8)
        XCTAssertFalse(verified.contains("123456"))
    }
}
