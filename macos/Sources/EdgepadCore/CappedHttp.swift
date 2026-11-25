import Foundation

/// A reply read by `CappedHttp`: its status, the first value of each response header (names lower-cased), and the
/// whole body.
public struct HttpReply: Equatable {
    public let status: Int
    public let headers: [String: String]
    public let body: Data

    public init(status: Int, headers: [String: String] = [:], body: Data = Data()) {
        self.status = status
        let lowered = headers.map { ($0.key.lowercased(), $0.value) }
        self.headers = Dictionary(lowered, uniquingKeysWith: { first, _ in first })
        self.body = body
    }

    public var isSuccess: Bool { (200...299).contains(status) }

    public func header(_ name: String) -> String? {
        headers[name.lowercased()]
    }
}

/// Sends one request and reads the reply under a cap. The dashboard's weather and GitHub clients take one, so a test
/// can stand in for the network.
public protocol HttpSender: AnyObject {
    /// The reply, or nil when the network failed, the deadline passed or the body was longer than `maxBytes`.
    func send(_ request: URLRequest, maxBytes: Int, what: String) async -> HttpReply?
}

/// One request to a fixed address the caller built from constants. Redirects are never followed (a 3xx comes back as
/// its status, which no caller accepts), the body is capped both by its announced length and by the bytes actually
/// read, and the request's own timeout bounds the exchange. Nothing about the request is logged: its address may carry
/// the user's city and its headers a token. The twin of the laptop core's CappedHttp.
public final class CappedHttp: HttpSender {
    private let session: URLSession

    /// The whole exchange, a slow trickle included, is cut off after this long; the request's own timeout only bounds
    /// the wait between bytes.
    static let totalTimeout: TimeInterval = 15

    public init(session: URLSession? = nil) {
        if let session {
            self.session = session
        } else {
            let configuration = URLSessionConfiguration.ephemeral
            configuration.timeoutIntervalForResource = Self.totalTimeout
            self.session = URLSession(configuration: configuration)
        }
    }

    public func send(_ request: URLRequest, maxBytes: Int, what: String) async -> HttpReply? {
        // Every request passes the read-only gate: nothing here can change anything on a host (see ReadOnlyGate).
        do {
            try ReadOnlyGate.verify(request)
        } catch {
            Log.write("\(what) failed: \(error)")
            return nil
        }
        do {
            let (bytes, response) = try await session.bytes(for: request, delegate: NoRedirects())
            guard let http = response as? HTTPURLResponse else { return nil }
            if http.expectedContentLength > Int64(maxBytes) {
                Log.write("\(what) failed: the reply announces more than allowed")
                return nil
            }
            var body = Data()
            for try await byte in bytes {
                if body.count >= maxBytes {
                    Log.write("\(what) failed: the reply is longer than allowed")
                    return nil
                }
                body.append(byte)
            }
            var headers: [String: String] = [:]
            for (key, value) in http.allHeaderFields {
                if let name = key as? String, let text = value as? String {
                    headers[name.lowercased()] = text
                }
            }
            return HttpReply(status: http.statusCode, headers: headers, body: body)
        } catch {
            if !(error is CancellationError) {
                Log.write("\(what) failed: \((error as NSError).domain) \((error as NSError).code)")
            }
            return nil
        }
    }
}

/// A task delegate that refuses every redirect, so a reply comes from the host asked or not at all. The capped
/// fetch, the speed test's upload and the update check and download all use it.
final class NoRedirects: NSObject, URLSessionTaskDelegate {
    func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        willPerformHTTPRedirection response: HTTPURLResponse,
        newRequest request: URLRequest,
        completionHandler: @escaping (URLRequest?) -> Void
    ) {
        completionHandler(nil)
    }
}

/// Percent-encoding the way .NET's `Uri.EscapeDataString` does it: everything but the unreserved characters of RFC 3986
/// (A-Z a-z 0-9 - . _ ~) is escaped as UTF-8, so a value cannot leave the parameter it was put in.
enum UrlText {
    private static let unreserved = CharacterSet(
        charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")

    static func escape(_ value: String) -> String {
        value.addingPercentEncoding(withAllowedCharacters: unreserved) ?? ""
    }
}
