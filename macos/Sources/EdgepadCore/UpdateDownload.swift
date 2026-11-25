import CryptoKit
import Foundation

/// Fetches release files, and nothing else. https only, from GitHub's own hosts, with every redirect followed by
/// hand so each hop is checked, and a byte cap enforced both on the announced length and on the bytes actually
/// read. Every failure returns nil and leaves no partial file; the caller then installs nothing.
public enum UpdateDownload {
    public static let maxSumsBytes = 64 * 1024
    public static let maxSignatureBytes = 1024
    public static let maxAssetBytes = 200 * 1024 * 1024

    private static let maxRedirects = 5
    private static let chunkBytes = 81920

    /// The body of `url`, at most `maxBytes` long; nil on any failure.
    public static func getBytes(_ url: URL, maxBytes: Int, session: URLSession = .shared) async -> Data? {
        do {
            let (bytes, _) = try await open(url, maxBytes: maxBytes, timeout: 30, session: session)
            var data = Data()
            for try await byte in bytes {
                if data.count >= maxBytes {
                    throw DownloadError.refused("The download is longer than allowed.")
                }
                data.append(byte)
            }
            return data
        } catch {
            Log.write("A release file could not be fetched: \(error.localizedDescription)")
            return nil
        }
    }

    /// Writes `url` to `destPath` + ".download" and returns its SHA-256, or nil on any failure (the half-written
    /// file is removed). It never renames: the caller moves the file into place only after the hash matches
    /// the signed list.
    public static func download(
        _ url: URL, to destPath: String, maxBytes: Int, progress: ((Int) -> Void)? = nil, session: URLSession = .shared
    ) async -> Data? {
        let temp = destPath + ".download"
        do {
            let (bytes, announced) = try await open(url, maxBytes: maxBytes, timeout: 20 * 60, session: session)
            guard FileManager.default.createFile(atPath: temp, contents: nil) else {
                throw DownloadError.refused("The file could not be created.")
            }
            let file = try FileHandle(forWritingTo: URL(fileURLWithPath: temp))
            defer { try? file.close() }
            var hasher = SHA256()
            var buffer = Data()
            var total = 0
            var last = -1
            for try await byte in bytes {
                buffer.append(byte)
                total += 1
                if total > maxBytes {
                    throw DownloadError.refused("The download is longer than allowed.")
                }
                if buffer.count >= chunkBytes {
                    try file.write(contentsOf: buffer)
                    hasher.update(data: buffer)
                    buffer.removeAll(keepingCapacity: true)
                    if announced > 0 {
                        let percent = min(100, Int(Int64(total) * 100 / announced))
                        if percent != last {
                            last = percent
                            progress?(percent)
                        }
                    }
                }
            }
            try file.write(contentsOf: buffer)
            hasher.update(data: buffer)
            return Data(hasher.finalize())
        } catch {
            Log.write("A release file could not be downloaded: \(error.localizedDescription)")
            try? FileManager.default.removeItem(atPath: temp)
            return nil
        }
    }

    /// Whether `url` may be fetched: https, no credentials or odd port, on GitHub's own hosts.
    public static func isAllowed(_ url: URL) -> Bool {
        guard url.scheme == "https", url.port == nil || url.port == 443, url.user == nil, url.password == nil,
            let host = url.host?.lowercased()
        else {
            return false
        }
        return host == "github.com" || host.hasSuffix(".githubusercontent.com")
    }

    private enum DownloadError: Error, LocalizedError {
        case refused(String)

        var errorDescription: String? {
            switch self {
            case .refused(let why): return why
            }
        }
    }

    /// Follows up to five redirects by hand, checking every hop. Returns the body stream and the announced
    /// length (-1 when none).
    private static func open(
        _ start: URL, maxBytes: Int, timeout: TimeInterval, session: URLSession
    ) async throws -> (URLSession.AsyncBytes, Int64) {
        var current = start
        for hop in 0...maxRedirects {
            guard isAllowed(current) else {
                throw DownloadError.refused("The address is not an https address on GitHub.")
            }
            let request = URLRequest(url: current, timeoutInterval: timeout)
            let (bytes, response) = try await session.bytes(for: request, delegate: NoRedirects())
            guard let http = response as? HTTPURLResponse else {
                throw DownloadError.refused("The answer was not HTTP.")
            }
            if [301, 302, 303, 307, 308].contains(http.statusCode) {
                guard let location = http.value(forHTTPHeaderField: "Location"),
                    let next = URL(string: location, relativeTo: current)?.absoluteURL
                else {
                    throw DownloadError.refused("A redirect named no address.")
                }
                if hop == maxRedirects {
                    throw DownloadError.refused("Too many redirects.")
                }
                current = next
                continue
            }
            guard (200...299).contains(http.statusCode) else {
                throw DownloadError.refused("The server answered \(http.statusCode).")
            }
            if http.expectedContentLength > Int64(maxBytes) {
                throw DownloadError.refused("The download announces more than allowed.")
            }
            return (bytes, http.expectedContentLength)
        }
        throw DownloadError.refused("Too many redirects.")
    }
}
