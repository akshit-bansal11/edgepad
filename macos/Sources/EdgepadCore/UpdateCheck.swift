import Foundation

/// A release version, exactly three numeric parts.
public struct Version: Comparable, CustomStringConvertible {
    public let major: Int
    public let minor: Int
    public let patch: Int

    /// Exactly three dot-separated parts, each one to four ASCII digits. Nothing else is a version, because
    /// the text came from the network.
    public init?(_ text: String) {
        let parts = text.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 3 else {
            return nil
        }
        var numbers: [Int] = []
        for part in parts {
            guard (1...4).contains(part.utf8.count), part.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }),
                let number = Int(part)
            else {
                return nil
            }
            numbers.append(number)
        }
        major = numbers[0]
        minor = numbers[1]
        patch = numbers[2]
    }

    public var description: String {
        "\(major).\(minor).\(patch)"
    }

    public static func < (lhs: Version, rhs: Version) -> Bool {
        (lhs.major, lhs.minor, lhs.patch) < (rhs.major, rhs.minor, rhs.patch)
    }
}

/// Whether GitHub holds a newer release than the one running. The twin of the Windows app's check, asked the
/// same way: the releases/latest page answers with a redirect to the newest tag, so one HEAD request and its
/// Location header say everything, with no JSON to read and no API rate limit to meet. It is the only thing
/// this app ever says to the internet, and it sends nothing about the Mac.
public enum UpdateCheck {
    public static let latestUrl = releases + "/latest"
    public static let every: TimeInterval = 24 * 60 * 60

    private static let releases = "https://github.com/akshit-bansal11/edgepad/releases"
    private static let tagUrl = releases + "/tag/v"

    /// The version a redirect `location` names. Anything but this repository's own tag page with a plain
    /// three-part version is refused: the answer is shown in the menu, and it came from the network.
    public static func versionIn(_ location: String?) -> Version? {
        guard let location, location.hasPrefix(tagUrl) else {
            return nil
        }
        return Version(String(location.dropFirst(tagUrl.count)))
    }

    /// Whether `candidate` is a later release than `running`. A suffix such as "-draft.38" is ignored, and a
    /// running version that cannot be read never counts as older.
    public static func isNewer(_ candidate: Version, than running: String) -> Bool {
        guard let core = running.split(separator: "-", omittingEmptySubsequences: false).first,
            let ours = Version(String(core))
        else {
            return false
        }
        return candidate > ours
    }

    /// The newest release's version; nil when GitHub could not be asked or gave no usable answer. The session
    /// refuses redirects so the 302 itself is the answer instead of the page it points at.
    public static func latest(completion: @escaping (Version?) -> Void) {
        guard let url = URL(string: latestUrl) else {
            completion(nil)
            return
        }
        var request = URLRequest(url: url, timeoutInterval: 10)
        request.httpMethod = "HEAD"
        let session = URLSession(configuration: .ephemeral, delegate: RedirectRefuser(), delegateQueue: nil)
        let task = session.dataTask(with: request) { _, response, error in
            session.finishTasksAndInvalidate()
            if let error {
                Log.write("Could not ask GitHub for the newest release: \(error.localizedDescription)")
                completion(nil)
                return
            }
            let location = (response as? HTTPURLResponse)?.value(forHTTPHeaderField: "Location")
            completion(versionIn(location))
        }
        task.resume()
    }
}

private final class RedirectRefuser: NSObject, URLSessionTaskDelegate {
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
