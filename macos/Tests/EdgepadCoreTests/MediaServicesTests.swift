import XCTest

@testable import EdgepadCore

final class MediaServicesTests: XCTestCase {
    func testTheTableEqualsTheSharedFile() throws {
        let file = try Fixtures.lines("media-services.txt").compactMap { line -> String? in
            guard line.hasPrefix("SERVICE ") else { return nil }
            let row = line.dropFirst(8).components(separatedBy: " #")[0]
            return row.trimmingCharacters(in: .whitespaces)
        }
        let code = MediaServices.table.map { "\($0.name)|\($0.matches.joined(separator: ","))" }
        XCTAssertEqual(file, code)
    }

    func testAHostIsTheHostOrASubDomain() {
        XCTAssertEqual(MediaServices.name(host: "www.netflix.com"), "Netflix")
        XCTAssertEqual(MediaServices.name(host: "music.youtube.com"), "YouTube Music")
        XCTAssertEqual(MediaServices.name(host: "www.youtube.com"), "YouTube")
        XCTAssertNil(MediaServices.name(host: "notnetflix.com"))
    }

    func testATitleWordStandsAlone() {
        XCTAssertEqual(MediaServices.name(title: "Prime Video: The Boys"), "Prime Video")
        XCTAssertNil(MediaServices.name(title: "Time complexity"))
    }

    func testADesktopAppIsFoundByItsBundleId() {
        XCTAssertEqual(MediaServices.name(bundleId: "com.apple.Music"), "Apple Music")
        XCTAssertEqual(MediaServices.name(bundleId: "com.spotify.client"), "Spotify")
        XCTAssertNil(MediaServices.name(bundleId: "org.videolan.vlc"))
    }
}
