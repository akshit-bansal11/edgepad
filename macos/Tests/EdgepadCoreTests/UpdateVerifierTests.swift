import CryptoKit
import XCTest

@testable import EdgepadCore

final class UpdateVerifierTests: XCTestCase {
    private let sums = Data("abc  Edgepad-1.0.0-mac.zip\n".utf8)

    private func sign(_ data: Data, with key: P256.Signing.PrivateKey) throws -> Data {
        try key.signature(for: data).derRepresentation
    }

    func testASignatureFromTheKeyIsAccepted() throws {
        let key = P256.Signing.PrivateKey()
        let signature = try sign(sums, with: key)

        XCTAssertTrue(
            UpdateVerifier.verifySums(sums, derSignature: signature, spki: key.publicKey.derRepresentation))
    }

    func testOneChangedByteOfTheSumsIsRefused() throws {
        let key = P256.Signing.PrivateKey()
        let signature = try sign(sums, with: key)
        var changed = sums
        changed[0] ^= 1

        XCTAssertFalse(
            UpdateVerifier.verifySums(changed, derSignature: signature, spki: key.publicKey.derRepresentation))
    }

    func testOneChangedByteOfTheSignatureIsRefused() throws {
        let key = P256.Signing.PrivateKey()
        var signature = try sign(sums, with: key)
        signature[signature.count - 1] ^= 1

        XCTAssertFalse(
            UpdateVerifier.verifySums(sums, derSignature: signature, spki: key.publicKey.derRepresentation))
    }

    func testASignatureByAnotherKeyIsRefused() throws {
        let signature = try sign(sums, with: P256.Signing.PrivateKey())

        XCTAssertFalse(
            UpdateVerifier.verifySums(
                sums, derSignature: signature, spki: P256.Signing.PrivateKey().publicKey.derRepresentation))
    }

    func testAP384KeyIsRefusedEvenWithItsOwnValidSignature() throws {
        let key = P384.Signing.PrivateKey()
        let signature = try key.signature(for: sums).derRepresentation

        XCTAssertFalse(
            UpdateVerifier.verifySums(sums, derSignature: signature, spki: key.publicKey.derRepresentation))
    }

    func testEmptyInputsAreRefused() throws {
        let key = P256.Signing.PrivateKey()
        let signature = try sign(sums, with: key)
        let spki = key.publicKey.derRepresentation

        XCTAssertFalse(UpdateVerifier.verifySums(Data(), derSignature: signature, spki: spki))
        XCTAssertFalse(UpdateVerifier.verifySums(sums, derSignature: Data(), spki: spki))
        XCTAssertFalse(UpdateVerifier.verifySums(sums, derSignature: signature, spki: Data()))
    }

    func testGarbageInputsAreRefusedWithoutCrashing() {
        XCTAssertFalse(
            UpdateVerifier.verifySums(sums, derSignature: Data([1, 2, 3]), spki: Data([4, 5, 6])))
    }

    func testTrailingBytesAfterTheKeyAreRefused() throws {
        let key = P256.Signing.PrivateKey()
        let signature = try sign(sums, with: key)
        let spki = key.publicKey.derRepresentation + Data([0])

        XCTAssertFalse(UpdateVerifier.verifySums(sums, derSignature: signature, spki: spki))
    }

    func testTheEmbeddedKeyIsTheSharedOneAndAP256Key() throws {
        XCTAssertEqual(UpdateKey.publicKeySpkiBase64, try Fixtures.lines("update-key.txt").first)
        XCTAssertTrue(UpdateKey.isSet)
        XCTAssertNoThrow(try P256.Signing.PublicKey(derRepresentation: UpdateKey.spki()))
    }

    func testTheHashOfAListedFileIsFoundByItsExactName() throws {
        let hash = String(repeating: "ab", count: 32)
        let list = "\(hash)  Edgepad-1.0.0-mac.zip\r\n\(String(repeating: "cd", count: 32)) *other.zip\n"

        let found = try XCTUnwrap(UpdateVerifier.hashFor(sums: list, fileName: "Edgepad-1.0.0-mac.zip"))

        XCTAssertEqual(found, Data(repeating: 0xab, count: 32))
        XCTAssertEqual(UpdateVerifier.hashFor(sums: list, fileName: "other.zip"), Data(repeating: 0xcd, count: 32))
    }

    func testANameThatIsMissingOrDifferentOrCarriesAPathIsRefused() {
        let hash = String(repeating: "ab", count: 32)

        XCTAssertNil(UpdateVerifier.hashFor(sums: "\(hash)  a.zip\n", fileName: "b.zip"))
        XCTAssertNil(UpdateVerifier.hashFor(sums: "\(hash)  a.zip\n", fileName: ""))
        XCTAssertNil(UpdateVerifier.hashFor(sums: "\(hash)  dir/a.zip\n", fileName: "dir/a.zip"))
        XCTAssertNil(UpdateVerifier.hashFor(sums: "\(hash)  a.zip\n", fileName: "..\\a.zip"))
        XCTAssertNil(UpdateVerifier.hashFor(sums: "\(hash)  dir/a.zip\n\(hash)  a.zip\n", fileName: "a.zip"))
    }

    func testANameListedTwiceIsRefused() {
        let hash = String(repeating: "ab", count: 32)

        XCTAssertNil(UpdateVerifier.hashFor(sums: "\(hash)  a.zip\n\(hash)  a.zip\n", fileName: "a.zip"))
    }

    func testAShortHashIsRefused() {
        XCTAssertNil(UpdateVerifier.hashFor(sums: "abcd  a.zip\n", fileName: "a.zip"))
    }

    func testUppercaseHexIsRefused() {
        let hash = String(repeating: "AB", count: 32)

        XCTAssertNil(UpdateVerifier.hashFor(sums: "\(hash)  a.zip\n", fileName: "a.zip"))
    }

    func testALineInAnyOtherShapeSpoilsTheWholeList() {
        let hash = String(repeating: "ab", count: 32)

        XCTAssertNil(UpdateVerifier.hashFor(sums: "\(hash)  a.zip\nnot a hash line\n", fileName: "a.zip"))
        XCTAssertNil(UpdateVerifier.hashFor(sums: "\(hash)\ta.zip\n", fileName: "a.zip"))
    }

    func testFilesAreHashedAndHashesComparedExactly() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        let body = Data("hello".utf8)
        try body.write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }

        let fileHash = try XCTUnwrap(UpdateVerifier.hashFile(at: url))

        XCTAssertEqual(fileHash, UpdateVerifier.hash(of: body))
        XCTAssertTrue(UpdateVerifier.hashesMatch(fileHash, UpdateVerifier.hash(of: body)))
        XCTAssertFalse(UpdateVerifier.hashesMatch(fileHash, UpdateVerifier.hash(of: Data("hellp".utf8))))
        XCTAssertFalse(UpdateVerifier.hashesMatch(fileHash, fileHash.dropLast()))
        XCTAssertNil(UpdateVerifier.hashFile(at: url.appendingPathExtension("missing")))
    }
}
