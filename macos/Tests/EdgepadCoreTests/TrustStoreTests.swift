import XCTest

@testable import EdgepadCore

final class TrustStoreTests: XCTestCase {
    private let directory = URL(fileURLWithPath: NSTemporaryDirectory())
        .appendingPathComponent("edgepad-tests-" + UUID().uuidString)

    private var trustPath: String {
        directory.appendingPathComponent("trusted-phone.txt").path
    }

    override func tearDown() {
        try? FileManager.default.removeItem(at: directory)
        super.tearDown()
    }

    private func writeTrustFile(_ text: String) throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try text.write(toFile: trustPath, atomically: true, encoding: .utf8)
    }

    private func readTrustFile() throws -> String {
        try String(contentsOfFile: trustPath, encoding: .utf8)
    }

    func testTheFirstPhoneIsTrustedAndRemembered() {
        let admitted = TrustStore(path: trustPath).admit("(AA:BB:CC:DD:EE:FF)", ask: yes)

        XCTAssertTrue(admitted)
        XCTAssertEqual(TrustStore(path: trustPath).trustedPhones, ["(AA:BB:CC:DD:EE:FF)"])
    }

    func testTheTrustedPhoneIsAdmittedWithoutAskingWhateverTheCase() {
        let store = TrustStore(path: trustPath)
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes)

        XCTAssertTrue(store.admit("(aa:bb:cc:dd:ee:ff)", ask: unasked))
    }

    func testASecondPhoneIsAskedAboutAndThenBothAreAdmittedUnasked() {
        let store = TrustStore(path: trustPath)
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes)
        var asked = false

        XCTAssertTrue(
            store.admit("(11:22:33:44:55:66)") {
                asked = true
                return true
            })

        XCTAssertTrue(asked)
        let reopened = TrustStore(path: trustPath)
        XCTAssertTrue(reopened.admit("(AA:BB:CC:DD:EE:FF)", ask: unasked))
        XCTAssertTrue(reopened.admit("(11:22:33:44:55:66)", ask: unasked))
        XCTAssertEqual(reopened.trustedPhones, ["(AA:BB:CC:DD:EE:FF)", "(11:22:33:44:55:66)"])
    }

    func testAFileFromBeforeThereCouldBeSeveralPhonesIsAListOfOne() throws {
        // As the old save wrote it: one address, no line break.
        try writeTrustFile("(AA:BB:CC:DD:EE:FF)")
        let store = TrustStore(path: trustPath)

        XCTAssertTrue(store.admit("(AA:BB:CC:DD:EE:FF)", ask: unasked))
        XCTAssertTrue(store.admit("(11:22:33:44:55:66)", ask: yes))
        XCTAssertEqual(try readTrustFile(), "(AA:BB:CC:DD:EE:FF)\n(11:22:33:44:55:66)\n")
    }

    func testLinesEndedEitherWayAndPaddedOrBlankAreRead() throws {
        try writeTrustFile(" (AA:BB:CC:DD:EE:FF) \r\n\r\n(11:22:33:44:55:66)\n")

        XCTAssertEqual(TrustStore(path: trustPath).trustedPhones, ["(AA:BB:CC:DD:EE:FF)", "(11:22:33:44:55:66)"])
    }

    func testAPhoneAlreadyInTheFileIsNotWrittenTwice() throws {
        let store = TrustStore(path: trustPath)
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes)

        // Added by hand while the owner was being asked about it.
        let admitted = store.admit("(11:22:33:44:55:66)") {
            try? writeTrustFile("(AA:BB:CC:DD:EE:FF)\n(11:22:33:44:55:66)\n")
            return true
        }

        XCTAssertTrue(admitted)
        XCTAssertTrue(store.admit("(aa:bb:cc:dd:ee:ff)", ask: unasked))
        XCTAssertEqual(try readTrustFile(), "(AA:BB:CC:DD:EE:FF)\n(11:22:33:44:55:66)\n")
    }

    func testAPhoneTheOwnerDoesNotTrustIsRefusedAndNotRemembered() {
        let store = TrustStore(path: trustPath)

        let admitted = store.admit("(AA:BB:CC:DD:EE:FF)", ask: no)

        XCTAssertFalse(admitted)
        XCTAssertEqual(store.trustedPhones, [])
    }

    func testAPhoneIsAskedAboutAgainAfterANo() {
        let store = TrustStore(path: trustPath)
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: no)

        XCTAssertTrue(store.admit("(11:22:33:44:55:66)", ask: yes))
        XCTAssertEqual(store.trustedPhones, ["(11:22:33:44:55:66)"])
    }

    func testForgetClearsEveryPhoneAndEachIsAskedAboutAgain() {
        let store = TrustStore(path: trustPath)
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes)
        _ = store.admit("(11:22:33:44:55:66)", ask: yes)

        XCTAssertTrue(store.forget())

        XCTAssertEqual(store.trustedPhones, [])
        XCTAssertFalse(store.canForget)
        var asked = false
        XCTAssertTrue(
            store.admit("(AA:BB:CC:DD:EE:FF)") {
                asked = true
                return true
            })
        XCTAssertTrue(asked)
    }

    func testAForgetWhileTheOwnerIsBeingAskedMakesAYesSaveNothing() {
        let store = TrustStore(path: trustPath)
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes)

        let admitted = store.admit("(11:22:33:44:55:66)") {
            store.forget()
            return true
        }

        XCTAssertFalse(admitted)
        XCTAssertEqual(store.trustedPhones, [])
        XCTAssertFalse(store.canForget)
        XCTAssertFalse(store.refuses("(11:22:33:44:55:66)"))
    }

    func testAForgetThatCannotDeleteTheFileRefusesEveryPhoneUntilOneCan() throws {
        // The phone is still named in the file, so a failed delete must not leave it trusted while the owner
        // believes it is gone. A directory that cannot be written is how a delete fails for a file that reads fine,
        // and a file that cannot be written as well is what stops it being emptied instead.
        let store = TrustStore(path: trustPath)
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes)
        try FileManager.default.setAttributes([.posixPermissions: 0o400], ofItemAtPath: trustPath)
        try FileManager.default.setAttributes([.posixPermissions: 0o500], ofItemAtPath: directory.path)
        let forgotten = store.forget()
        // Restored before anything else can fail, or tearDown could not remove the directory.
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: directory.path)
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: trustPath)

        XCTAssertFalse(forgotten)
        // Readable again and still naming the phone, yet refused.
        XCTAssertTrue(store.refuses("(AA:BB:CC:DD:EE:FF)"))
        XCTAssertFalse(store.admit("(AA:BB:CC:DD:EE:FF)", ask: unasked))
        XCTAssertEqual(store.trustedPhones, [])
        XCTAssertTrue(store.canForget)

        XCTAssertTrue(store.forget())
        XCTAssertFalse(store.refuses("(AA:BB:CC:DD:EE:FF)"))
        XCTAssertTrue(store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes))
    }

    func testAFileThatCannotBeDeletedIsEmptiedInstead() throws {
        // Only the directory is read-only, so the delete fails while the file itself can still be written.
        let store = TrustStore(path: trustPath)
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes)
        try FileManager.default.setAttributes([.posixPermissions: 0o500], ofItemAtPath: directory.path)
        let forgotten = store.forget()
        // Restored before anything else can fail, or tearDown could not remove the directory.
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: directory.path)

        XCTAssertTrue(forgotten)
        XCTAssertEqual(try String(contentsOfFile: trustPath, encoding: .utf8), "")
        XCTAssertEqual(store.trustedPhones, [])
        XCTAssertFalse(store.canForget)
        XCTAssertTrue(store.admit("(11:22:33:44:55:66)", ask: yes))
    }

    func testForgettingWhatIsNotThereIsNotAnError() {
        let store = TrustStore(path: trustPath)

        store.forget()
        store.forget()

        XCTAssertEqual(store.trustedPhones, [])
    }

    func testABlankFileMeansNothingIsTrustedYet() throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try "   \r\n".write(toFile: trustPath, atomically: true, encoding: .utf8)
        let store = TrustStore(path: trustPath)

        XCTAssertEqual(store.trustedPhones, [])
        XCTAssertTrue(store.admit("(11:22:33:44:55:66)", ask: yes))
    }

    func testATrustFileThatCannotBeReadRefusesAndNeverAsks() throws {
        // A directory where the file should be exists and cannot be read as text, which is not the same as no
        // file: treating it as absent would re-arm trust on first use.
        try FileManager.default.createDirectory(atPath: trustPath, withIntermediateDirectories: true)
        let store = TrustStore(path: trustPath)

        XCTAssertFalse(store.admit("(AA:BB:CC:DD:EE:FF)", ask: unasked))
        XCTAssertTrue(store.refuses("(AA:BB:CC:DD:EE:FF)"))
        XCTAssertEqual(store.trustedPhones, [])
    }

    func testAPhoneThatCouldNotBeSavedIsStillTrustedForThisRun() throws {
        // A file where the folder should be: the read finds nothing, and the write cannot create the folder.
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let blocked = directory.appendingPathComponent("blocked").path
        try "".write(toFile: blocked, atomically: true, encoding: .utf8)
        let store = TrustStore(path: blocked + "/trusted-phone.txt")

        XCTAssertTrue(store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes))
        XCTAssertTrue(store.admit("(aa:bb:cc:dd:ee:ff)", ask: unasked))
        XCTAssertFalse(store.admit("(11:22:33:44:55:66)", ask: no))
        XCTAssertEqual(store.trustedPhones, ["(AA:BB:CC:DD:EE:FF)"])
        XCTAssertTrue(store.canForget)
    }

    func testAPhoneNotTrustedYetIsNotRefusedUpFront() {
        let store = TrustStore(path: trustPath)
        XCTAssertFalse(store.refuses("(11:22:33:44:55:66)"))

        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes)

        XCTAssertFalse(store.refuses("(aa:bb:cc:dd:ee:ff)"))
        XCTAssertFalse(store.refuses("(11:22:33:44:55:66)"))
    }

    func testADeviceArrivingWhileTheOwnerIsAskedIsRefusedWithoutBeingAsked() {
        let store = TrustStore(path: trustPath)
        var refusedWhileAsking = false

        _ = store.admit("(AA:BB:CC:DD:EE:FF)") {
            refusedWhileAsking = store.refuses("11:22:33:44:55:66")
            return true
        }

        XCTAssertTrue(refusedWhileAsking)
    }

    func testNothingIsRefusedOnceTheQuestionIsAnsweredNo() {
        let store = TrustStore(path: trustPath)

        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: no)

        XCTAssertFalse(store.refuses("(11:22:33:44:55:66)"))
    }

    func testAPhoneJustDeclinedIsRefusedUnaskedUntilTheQuietPeriodPasses() {
        var time: TimeInterval = 0
        let store = TrustStore(path: trustPath, now: { time })
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: no)

        XCTAssertTrue(store.refuses("(aa:bb:cc:dd:ee:ff)"))
        XCTAssertFalse(store.admit("(AA:BB:CC:DD:EE:FF)", ask: unasked))

        time = TrustStore.quietAfterDeclineSeconds
        var asked = false
        XCTAssertTrue(
            store.admit("(AA:BB:CC:DD:EE:FF)") {
                asked = true
                return true
            })
        XCTAssertTrue(asked)
    }

    func testADeclineForOnePhoneDoesNotStopAnotherBeingAsked() {
        let store = TrustStore(path: trustPath, now: { 0 })
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: no)

        XCTAssertFalse(store.refuses("(11:22:33:44:55:66)"))
        XCTAssertTrue(store.admit("(11:22:33:44:55:66)", ask: yes))
    }

    func testForgetClearsTheDecline() {
        let store = TrustStore(path: trustPath, now: { 0 })
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: no)

        store.forget()

        XCTAssertFalse(store.refuses("(AA:BB:CC:DD:EE:FF)"))
        XCTAssertTrue(store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes))
    }

    func testForgetIsOfferedOnlyWhenThereIsSomethingToClear() throws {
        let store = TrustStore(path: trustPath)
        XCTAssertFalse(store.canForget)

        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes)
        XCTAssertTrue(store.canForget)

        store.forget()
        XCTAssertFalse(store.canForget)

        try FileManager.default.createDirectory(atPath: trustPath, withIntermediateDirectories: true)
        XCTAssertTrue(store.canForget)
    }

    func testTheTrustFileIsPrivateToTheUser() throws {
        _ = TrustStore(path: trustPath).admit("(AA:BB:CC:DD:EE:FF)", ask: yes)

        let fileMode = try FileManager.default.attributesOfItem(atPath: trustPath)[.posixPermissions] as? NSNumber
        let directoryMode =
            try FileManager.default.attributesOfItem(atPath: directory.path)[.posixPermissions]
            as? NSNumber

        XCTAssertEqual(fileMode?.intValue, 0o600)
        XCTAssertEqual(directoryMode?.intValue, 0o700)
    }

    func testASecondPhoneWhileTheOwnerIsBeingAskedIsRefusedUnasked() {
        let store = TrustStore(path: trustPath)
        var second = true

        let first = store.admit("(AA:BB:CC:DD:EE:FF)") {
            second = store.admit("(11:22:33:44:55:66)", ask: unasked)
            return true
        }

        XCTAssertTrue(first)
        XCTAssertFalse(second)
        XCTAssertEqual(store.trustedPhones, ["(AA:BB:CC:DD:EE:FF)"])
    }
}

private func yes() -> Bool {
    true
}

private func no() -> Bool {
    false
}

private func unasked() -> Bool {
    XCTFail("the owner was asked when nothing needed asking")
    return false
}
