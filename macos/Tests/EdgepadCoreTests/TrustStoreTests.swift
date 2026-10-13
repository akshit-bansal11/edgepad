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

    func testTheFirstPhoneIsTrustedAndRemembered() {
        let admitted = TrustStore(path: trustPath).admit("(AA:BB:CC:DD:EE:FF)", ask: yes)

        XCTAssertTrue(admitted)
        XCTAssertEqual(TrustStore(path: trustPath).trusted, "(AA:BB:CC:DD:EE:FF)")
    }

    func testTheTrustedPhoneIsAdmittedWithoutAskingWhateverTheCase() {
        let store = TrustStore(path: trustPath)
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes)

        XCTAssertTrue(store.admit("(aa:bb:cc:dd:ee:ff)", ask: unasked))
    }

    func testAnotherPhoneIsRefusedWithoutAsking() {
        let store = TrustStore(path: trustPath)
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes)

        XCTAssertFalse(store.admit("(11:22:33:44:55:66)", ask: unasked))
    }

    func testAPhoneTheOwnerDoesNotTrustIsRefusedAndNotRemembered() {
        let store = TrustStore(path: trustPath)

        let admitted = store.admit("(AA:BB:CC:DD:EE:FF)", ask: no)

        XCTAssertFalse(admitted)
        XCTAssertNil(store.trusted)
    }

    func testAPhoneIsAskedAboutAgainAfterANo() {
        let store = TrustStore(path: trustPath)
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: no)

        XCTAssertTrue(store.admit("(11:22:33:44:55:66)", ask: yes))
        XCTAssertEqual(store.trusted, "(11:22:33:44:55:66)")
    }

    func testForgetLetsTheNextPhoneBecomeTrusted() {
        let store = TrustStore(path: trustPath)
        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes)

        store.forget()

        XCTAssertNil(store.trusted)
        XCTAssertTrue(store.admit("(11:22:33:44:55:66)", ask: yes))
    }

    func testForgettingWhatIsNotThereIsNotAnError() {
        let store = TrustStore(path: trustPath)

        store.forget()
        store.forget()

        XCTAssertNil(store.trusted)
    }

    func testABlankFileMeansNothingIsTrustedYet() throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try "   \r\n".write(toFile: trustPath, atomically: true, encoding: .utf8)
        let store = TrustStore(path: trustPath)

        XCTAssertNil(store.trusted)
        XCTAssertTrue(store.admit("(11:22:33:44:55:66)", ask: yes))
    }

    func testATrustFileThatCannotBeReadRefusesAndNeverAsks() throws {
        // A directory where the file should be exists and cannot be read as text, which is not the same as no
        // file: treating it as absent would re-arm trust on first use.
        try FileManager.default.createDirectory(atPath: trustPath, withIntermediateDirectories: true)
        let store = TrustStore(path: trustPath)

        XCTAssertFalse(store.admit("(AA:BB:CC:DD:EE:FF)", ask: unasked))
        XCTAssertTrue(store.refuses("(AA:BB:CC:DD:EE:FF)"))
        XCTAssertNil(store.trusted)
    }

    func testAPhoneThatCouldNotBeSavedIsStillTheOnlyOneTrusted() throws {
        // A file where the folder should be: the read finds nothing, and the write cannot create the folder.
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let blocked = directory.appendingPathComponent("blocked").path
        try "".write(toFile: blocked, atomically: true, encoding: .utf8)
        let store = TrustStore(path: blocked + "/trusted-phone.txt")

        XCTAssertTrue(store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes))
        XCTAssertFalse(store.admit("(11:22:33:44:55:66)", ask: yes))
        XCTAssertTrue(store.refuses("(11:22:33:44:55:66)"))
        XCTAssertEqual(store.trusted, "(AA:BB:CC:DD:EE:FF)")
    }

    func testRefusesOnlyWhatTheSessionWouldRefuse() {
        let store = TrustStore(path: trustPath)
        XCTAssertFalse(store.refuses("(11:22:33:44:55:66)"))

        _ = store.admit("(AA:BB:CC:DD:EE:FF)", ask: yes)

        XCTAssertFalse(store.refuses("(aa:bb:cc:dd:ee:ff)"))
        XCTAssertTrue(store.refuses("(11:22:33:44:55:66)"))
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
        XCTAssertEqual(store.trusted, "(AA:BB:CC:DD:EE:FF)")
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
