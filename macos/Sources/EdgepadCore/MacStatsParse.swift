import Foundation

/// The pure parts of the Mac's system stats (macos/Sources/Edgepad/MacStats.swift), here so the tests can reach them.
public enum MacStatsParse {
    /// The busy share of the ticks between two readings, 0 to 100 rounded, or -1 when no tick passed.
    public static func cpuPercent(busy: UInt64, total: UInt64) -> Int {
        guard total > 0 else { return -1 }
        return Int(min(100, (min(busy, total) * 100 + total / 2) / total))
    }

    /// KiB per second for `bytes` over `seconds`, held to what an Int32 carries; 0 when no time passed.
    public static func kibPerSecond(bytes: UInt64, seconds: Double) -> Int {
        guard seconds > 0 else { return 0 }
        return Int(min(Double(Int32.max), Double(bytes) / 1024 / seconds))
    }

    /// The gateway and interface of `/sbin/route -n get default`, or nil when it names no gateway.
    public static func route(_ text: String) -> (gateway: String, interface: String)? {
        var gateway = ""
        var interface = ""
        for line in text.split(whereSeparator: \.isNewline) {
            let parts = line.split(separator: ":", maxSplits: 1)
            guard parts.count == 2 else { continue }
            let key = parts[0].trimmingCharacters(in: .whitespaces)
            let value = parts[1].trimmingCharacters(in: .whitespaces)
            if key == "gateway" { gateway = value }
            if key == "interface" { interface = value }
        }
        return gateway.isEmpty ? nil : (gateway, interface)
    }

    /// True for a dotted IPv4 address: the only thing ever passed to /sbin/ping, so nothing else reaches its argv.
    public static func isIPv4(_ text: String) -> Bool {
        let parts = text.split(separator: ".", omittingEmptySubsequences: false)
        return parts.count == 4 && parts.allSatisfy(isOctet)
    }

    /// The round trip of one `/sbin/ping` reply ("time=12.345 ms") in whole milliseconds, or nil when it has none.
    public static func pingMs(_ text: String) -> Int? {
        guard let start = text.range(of: "time=") else { return nil }
        let number = text[start.upperBound...].prefix { ("0"..."9").contains($0) || $0 == "." }
        guard let ms = Double(number), ms >= 0 else { return nil }
        return Int(min(ms, 60_000).rounded())
    }

    private static func isOctet(_ part: Substring) -> Bool {
        (1...3).contains(part.count) && part.allSatisfy { ("0"..."9").contains($0) } && UInt8(part) != nil
    }
}
