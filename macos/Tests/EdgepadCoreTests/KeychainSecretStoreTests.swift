import XCTest

@testable import EdgepadCore

/// Runs against the real Keychain under a service of its own, removed afterwards. A runner with no usable Keychain
/// skips rather than fails: the point is the round trip where there is one.
final class KeychainSecretStoreTests: XCTestCase {
    private let service = "me.akshitbansal.edgepad.tests.\(UUID().uuidString)"
    private let names = ["github.token", "other"]

    private var store: KeychainSecretStore { KeychainSecretStore(service: service) }

    override func tearDown() {
        for name in names {
            store.delete(name)
        }
    }

    private func usable() throws {
        guard store.set("other", "probe") else {
            throw XCTSkip("No usable Keychain on this machine")
        }
        store.delete("other")
    }

    func testASecretRoundTripsAndIsReplaced() throws {
        try usable()

        XCTAssertTrue(store.set("github.token", "gho_first"))
        XCTAssertEqual(store.get("github.token"), "gho_first")

        XCTAssertTrue(store.set("github.token", "gho_second"))
        XCTAssertEqual(KeychainSecretStore(service: service).get("github.token"), "gho_second")
    }

    func testDeleteRemovesItAndAMissingNameReadsAsNil() throws {
        try usable()
        XCTAssertTrue(store.set("github.token", "gho_x"))

        store.delete("github.token")
        store.delete("github.token")

        XCTAssertNil(store.get("github.token"))
        XCTAssertNil(store.get("never-saved"))
    }

    func testTheAppsServiceIsNeverTouchedByAnotherService() throws {
        try usable()
        XCTAssertTrue(store.set("github.token", "gho_mine"))
        XCTAssertNil(KeychainSecretStore(service: service + ".other").get("github.token"))
    }

    func testABadNameIsRefusedWithoutAsking() {
        XCTAssertFalse(store.set("", "x"))
        XCTAssertFalse(store.set("has space", "x"))
        XCTAssertFalse(store.set(String(repeating: "a", count: 101), "x"))
        XCTAssertNil(store.get(""))
    }

    func testTheDefaultServiceIsTheAppsBundleId() {
        XCTAssertEqual(KeychainSecretStore.defaultService, "me.akshitbansal.edgepad")
    }
}
