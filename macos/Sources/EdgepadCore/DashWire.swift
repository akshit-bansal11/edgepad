import Foundation

/// TEXT STATS. Percents are 0..100, down and up are KiB/s, ping is ms, temperatures are C, power is W; -1 is unknown.
public struct StatsSample: Equatable {
    public let cpu: Int
    public let ram: Int
    public let gpu: Int
    public let vram: Int
    public let down: Int
    public let up: Int
    public let ping: Int
    public let loss: Int
    public let cpuTemp: Int
    public let gpuTemp: Int
    public let gpuPower: Int

    public init(
        cpu: Int, ram: Int, gpu: Int, vram: Int, down: Int, up: Int, ping: Int, loss: Int, cpuTemp: Int, gpuTemp: Int,
        gpuPower: Int
    ) {
        self.cpu = cpu
        self.ram = ram
        self.gpu = gpu
        self.vram = vram
        self.down = down
        self.up = up
        self.ping = ping
        self.loss = loss
        self.cpuTemp = cpuTemp
        self.gpuTemp = gpuTemp
        self.gpuPower = gpuPower
    }
}

/// TEXT STATS_INFO: the names the numbers belong to and the memory sizes, -1 unknown.
public struct StatsInfo: Equatable {
    public let cpuName: String
    public let ramMiB: Int
    public let gpuName: String
    public let vramMiB: Int
    public let netName: String

    public init(cpuName: String, ramMiB: Int, gpuName: String, vramMiB: Int, netName: String) {
        self.cpuName = cpuName
        self.ramMiB = ramMiB
        self.gpuName = gpuName
        self.vramMiB = vramMiB
        self.netName = netName
    }
}

/// One disk of TEXT DISKS.
public struct DiskEntry: Equatable {
    public let label: String
    public let usedPct: Int
    public let totalGiB: Int64
    public let freeGiB: Int64

    public init(label: String, usedPct: Int, totalGiB: Int64, freeGiB: Int64) {
        self.label = label
        self.usedPct = usedPct
        self.totalGiB = totalGiB
        self.freeGiB = freeGiB
    }
}

/// One session of TEXT SESSIONS. `agent` is an `AgentToken` raw value and `state` a `SessionStateToken` raw value.
public struct SessionEntry: Equatable {
    public let agent: String
    public let state: String
    public let title: String

    public init(agent: String, state: String, title: String) {
        self.agent = agent
        self.state = state
        self.title = title
    }
}

/// One limit of TEXT LIMITS. `window` is minutes, `LimitWindow.context`, or `LimitWindow.namedPrefix` and a name.
public struct LimitEntry: Equatable {
    public let agent: String
    public let window: String
    public let pct: Double
    public let resetAtMin: Int64
    public let measuredAtMin: Int64

    public init(agent: String, window: String, pct: Double, resetAtMin: Int64, measuredAtMin: Int64) {
        self.agent = agent
        self.window = window
        self.pct = pct
        self.resetAtMin = resetAtMin
        self.measuredAtMin = measuredAtMin
    }
}

/// One agent of TEXT SPEND. Cents are estimates at API list prices.
public struct SpendEntry: Equatable {
    public let agent: String
    public let measuredAtMin: Int64
    public let tokToday: Int64
    public let centsToday: Int64
    public let tokWeek: Int64
    public let centsWeek: Int64
    public let tokMonth: Int64
    public let centsMonth: Int64

    public init(
        agent: String, measuredAtMin: Int64, tokToday: Int64, centsToday: Int64, tokWeek: Int64, centsWeek: Int64,
        tokMonth: Int64, centsMonth: Int64
    ) {
        self.agent = agent
        self.measuredAtMin = measuredAtMin
        self.tokToday = tokToday
        self.centsToday = centsToday
        self.tokWeek = tokWeek
        self.centsWeek = centsWeek
        self.tokMonth = tokMonth
        self.centsMonth = centsMonth
    }
}

/// TEXT WEATHER.
public struct WeatherInfo: Equatable {
    public let tempTenthsC: Int
    public let wmoCode: Int
    public let city: String

    public init(tempTenthsC: Int, wmoCode: Int, city: String) {
        self.tempTenthsC = tempTenthsC
        self.wmoCode = wmoCode
        self.city = city
    }
}

/// One line of BLOB GITHUB. `section` is R, M or N; `ci` is a `CiToken` raw value.
public struct GitHubLine: Equatable {
    public let section: String
    public let repo: String
    public let number: Int64
    public let title: String
    public let draft: Bool
    public let ci: String

    public init(section: String, repo: String, number: Int64, title: String, draft: Bool, ci: String) {
        self.section = section
        self.repo = repo
        self.number = number
        self.title = title
        self.draft = draft
        self.ci = ci
    }
}

/// The body of a BLOB PERM_ASK once read: who asks, with which tool, and the cleaned text.
public struct PermAsk: Equatable {
    public let agent: String
    public let tool: String
    public let text: String

    public init(agent: String, tool: String, text: String) {
        self.agent = agent
        self.tool = tool
        self.text = text
    }
}

/// A TEXT PERM_ANSWER once read.
public struct PermAnswer: Equatable {
    public let id: String
    public let allow: Bool

    public init(id: String, allow: Bool) {
        self.id = id
        self.allow = allow
    }

    /// Exactly 12 lowercase hex digits, "/", then "a" or "d". Anything else, a trailing space or upper case included,
    /// is refused.
    public static func parse(_ wire: String) -> PermAnswer? {
        let scalars = Array(wire.unicodeScalars)
        guard scalars.count == 14, scalars[12] == "/", DashWire.isId(scalars[0..<12]) else { return nil }
        guard scalars[13] == "a" || scalars[13] == "d" else { return nil }
        return PermAnswer(id: String(String.UnicodeScalarView(scalars[0..<12])), allow: scalars[13] == "a")
    }
}

/// The text and blob codecs of the dashboard, byte for byte what protocol/dashboard-vectors.txt holds and what the
/// laptop core's DashWire.cs and the Kotlin codecs produce. Encoders clean and cap what they are given, so a caller
/// cannot send a payload that breaks its own format; readers drop an entry they cannot read instead of failing the
/// list. Everything works on Unicode scalars, never on Characters, so "\r\n" or a combining mark after a separator
/// splits exactly as the other ports split it.
public enum DashWire {
    static let maxText = 255
    static let maxDisks = 6
    static let maxSessions = 8

    /// Control characters (Cc), space, "|", "/", ":" and the invisible characters (`isBidi`) become a space, runs
    /// collapse, then trim and cut to `limit` code points.
    public static func clean(_ text: String, _ limit: Int) -> String {
        var collapsed = String.UnicodeScalarView()
        var gap = false
        for scalar in text.unicodeScalars {
            if scalar == " " || scalar == "|" || scalar == "/" || scalar == ":" || isControl(scalar) || isBidi(scalar) {
                gap = !collapsed.isEmpty
                continue
            }
            if gap {
                collapsed.append(" ")
                gap = false
            }
            collapsed.append(scalar)
        }
        var kept = String.UnicodeScalarView(collapsed.prefix(max(0, limit)))
        while kept.last == " " {
            kept.removeLast()
        }
        return String(kept)
    }

    /// For an approval's text: keeps line feeds and tabs, and shows every other control character and every invisible
    /// character as <U+XXXX>, so it can be read but never takes effect (Trojan Source) and nothing the command holds is
    /// hidden (a carriage return or a NUL included). No trim and no cut.
    public static func cleanText(_ text: String) -> String {
        var clean = ""
        for scalar in text.unicodeScalars {
            if scalar == "\n" || scalar == "\t" {
                clean.unicodeScalars.append(scalar)
            } else if isBidi(scalar) || isControl(scalar) {
                let hex = String(scalar.value, radix: 16, uppercase: true)
                clean += "<U+" + String(repeating: "0", count: max(0, 4 - hex.count)) + hex + ">"
            } else {
                clean.unicodeScalars.append(scalar)
            }
        }
        return clean
    }

    /// The entries joined by `separator`, the last ones dropped until the result is at most 255 bytes.
    public static func fit255(_ parts: [String], _ separator: String) -> String {
        fit(parts, separator, maxText)
    }

    /// True for exactly 12 lowercase hex digits.
    static func isId(_ id: ArraySlice<Unicode.Scalar>) -> Bool {
        id.count == 12 && id.allSatisfy { ("0"..."9").contains($0) || ("a"..."f").contains($0) }
    }

    // ---- STATS

    /// "cpu/ram/gpu/vram/down/up/ping/loss/ctemp/gtemp/gpower": percents clamp to 0..100 and any negative is -1.
    public static func encodeStats(_ s: StatsSample) -> String {
        let percents = [pct(s.cpu), pct(s.ram), pct(s.gpu), pct(s.vram)]
        let rest = [num(s.down), num(s.up), num(s.ping), pct(s.loss), num(s.cpuTemp), num(s.gpuTemp), num(s.gpuPower)]
        return (percents + rest).joined(separator: "/")
    }

    public static func parseStats(_ wire: String) -> StatsSample? {
        let parts = split(wire, "/")
        guard parts.count == 11 else { return nil }
        var n: [Int] = []
        for part in parts {
            guard let value = int(part) else { return nil }
            n.append(value)
        }
        return StatsSample(
            cpu: n[0], ram: n[1], gpu: n[2], vram: n[3], down: n[4], up: n[5], ping: n[6], loss: n[7], cpuTemp: n[8],
            gpuTemp: n[9], gpuPower: n[10])
    }

    // ---- STATS_INFO

    /// "cpuName|ramMiB|gpuName|vramMiB|netName", the names cleaned to 30 code points and, when the result passes 255
    /// bytes, shortened together a code point at a time.
    public static func encodeStatsInfo(_ info: StatsInfo) -> String {
        var text = ""
        for limit in stride(from: 30, through: 0, by: -1) {
            let names = [clean(info.cpuName, limit), clean(info.gpuName, limit), clean(info.netName, limit)]
            text = [names[0], num(info.ramMiB), names[1], num(info.vramMiB), names[2]].joined(separator: "|")
            if text.utf8.count <= maxText { break }
        }
        return text
    }

    public static func parseStatsInfo(_ wire: String) -> StatsInfo? {
        let parts = split(wire, "|")
        guard parts.count == 5, let ram = int(parts[1]), let vram = int(parts[3]) else { return nil }
        return StatsInfo(cpuName: parts[0], ramMiB: ram, gpuName: parts[2], vramMiB: vram, netName: parts[4])
    }

    // ---- DISKS

    /// "label:usedPct:totalGiB:freeGiB" entries joined by "|", at most 6, the last dropped until it fits 255 bytes.
    public static func encodeDisks(_ disks: [DiskEntry]) -> String {
        let parts = disks.prefix(maxDisks).map { d -> String in
            let label = clean(d.label, 16)
            return "\(label.isEmpty ? "-" : label):\(pct(d.usedPct)):\(big(d.totalGiB, -1)):\(big(d.freeGiB, -1))"
        }
        return fit255(parts, "|")
    }

    /// An entry that does not read is skipped.
    public static func parseDisks(_ wire: String) -> [DiskEntry] {
        entries(wire) { entry in
            let p = split(entry, ":")
            guard p.count == 4, let used = int(p[1]), let total = long(p[2]), let free = long(p[3]) else { return nil }
            return DiskEntry(label: p[0], usedPct: used, totalGiB: total, freeGiB: free)
        }
    }

    // ---- SESSIONS

    /// "agent:state:title" entries joined by "|", newest first as given, at most 8; an unknown agent or state drops
    /// the entry.
    public static func encodeSessions(_ sessions: [SessionEntry]) -> String {
        var parts: [String] = []
        for s in sessions where parts.count < maxSessions && isAgent(s.agent) && isState(s.state) {
            parts.append("\(s.agent):\(s.state):\(clean(s.title, 24))")
        }
        return fit255(parts, "|")
    }

    public static func parseSessions(_ wire: String) -> [SessionEntry] {
        entries(wire) { entry in
            let p = split(entry, ":", limit: 3)
            guard p.count == 3, isAgent(p[0]), isState(p[1]) else { return nil }
            return SessionEntry(agent: p[0], state: p[1], title: p[2])
        }
    }

    // ---- PERM_ANSWER, PERM_ASK

    /// The label of a BLOB PERM_ASK: "id/f", f 1 when the text is complete.
    public static func permLabel(_ id: String, complete: Bool) -> String {
        "\(id)/\(complete ? 1 : 0)"
    }

    public static func parsePermLabel(_ label: String) -> (id: String, complete: Bool)? {
        let scalars = Array(label.unicodeScalars)
        guard scalars.count == 14, scalars[12] == "/", isId(scalars[0..<12]) else { return nil }
        guard scalars[13] == "0" || scalars[13] == "1" else { return nil }
        return (String(String.UnicodeScalarView(scalars[0..<12])), scalars[13] == "1")
    }

    // ---- LIMITS

    /// "agent:window:pct:resetAtMin:measuredAtMin" entries joined by "|". An entry with an unknown agent or a window
    /// outside the three forms is dropped. The percent clamps to 0..100 and rounds half up.
    public static func encodeLimits(_ limits: [LimitEntry]) -> String {
        var parts: [String] = []
        for l in limits where isAgent(l.agent) {
            guard let canonical = window(l.window) else { continue }
            let pct = l.pct.isNaN ? 0 : Int64((min(max(l.pct, 0), 100) + 0.5).rounded(.down))
            parts.append("\(l.agent):\(canonical):\(num(pct)):\(big(l.resetAtMin, -1)):\(big(l.measuredAtMin, -1))")
        }
        return fit255(parts, "|")
    }

    public static func parseLimits(_ wire: String) -> [LimitEntry] {
        entries(wire) { entry in
            let p = split(entry, ":")
            guard p.count == 5, isAgent(p[0]), let canonical = window(p[1]), let pct = long(p[2]) else { return nil }
            guard let reset = long(p[3]), let measured = long(p[4]) else { return nil }
            let clamped = Double(min(max(pct, 0), 100))
            return LimitEntry(agent: p[0], window: canonical, pct: clamped, resetAtMin: reset, measuredAtMin: measured)
        }
    }

    /// The canonical form of a window: decimal minutes 1..525600 without a leading zero, "ctx", or "~" and a name
    /// cleaned to 16 code points that is not empty. Nil for anything else.
    public static func window(_ window: String) -> String? {
        if window == LimitWindow.context { return window }
        let scalars = Array(window.unicodeScalars)
        if scalars.first == "~" {
            let name = clean(String(String.UnicodeScalarView(scalars.dropFirst())), 16)
            return name.isEmpty ? nil : LimitWindow.namedPrefix + name
        }
        guard (1...6).contains(scalars.count), scalars[0] != "0", scalars.allSatisfy(isDigit) else { return nil }
        guard let minutes = Int(window), minutes <= 525_600 else { return nil }
        return window
    }

    // ---- SPEND
    // TODO(unpriced): mirror the "unpriced" field the core agent adds to the SPEND wire (DashWire.cs,
    // protocol/dashboard-vectors.txt) into SpendEntry, encodeSpend, SpendBoard and IntegrationHost once it lands.

    /// "agent/measuredAtMin/tokToday/centsToday/tokWeek/centsWeek/tokMonth/centsMonth" entries joined by "|".
    public static func encodeSpend(_ spend: [SpendEntry]) -> String {
        let parts = spend.filter { isAgent($0.agent) }.map { s -> String in
            let today = [big(s.tokToday, 0), big(s.centsToday, 0)]
            let later = [big(s.tokWeek, 0), big(s.centsWeek, 0), big(s.tokMonth, 0), big(s.centsMonth, 0)]
            return ([s.agent, big(s.measuredAtMin, -1)] + today + later).joined(separator: "/")
        }
        return fit255(parts, "|")
    }

    public static func parseSpend(_ wire: String) -> [SpendEntry] {
        entries(wire) { entry in
            let p = split(entry, "/")
            guard p.count == 8, isAgent(p[0]) else { return nil }
            var n: [Int64] = []
            for part in p.dropFirst() {
                guard let value = long(part) else { return nil }
                n.append(value)
            }
            return SpendEntry(
                agent: p[0], measuredAtMin: n[0], tokToday: n[1], centsToday: n[2], tokWeek: n[3], centsWeek: n[4],
                tokMonth: n[5], centsMonth: n[6])
        }
    }

    // ---- WEATHER

    /// "tempTenthsC/wmoCode/city"; nil is the empty payload, meaning no weather.
    public static func encodeWeather(_ weather: WeatherInfo?) -> String {
        guard let weather else { return "" }
        let wmo = (0...99).contains(weather.wmoCode) ? weather.wmoCode : -1
        return "\(min(max(weather.tempTenthsC, -999), 999))/\(wmo)/\(clean(weather.city, 24))"
    }

    public static func parseWeather(_ wire: String) -> WeatherInfo? {
        let p = split(wire, "/", limit: 3)
        guard p.count == 3, let temp = int(p[0]), let wmo = int(p[1]) else { return nil }
        let code = (0...99).contains(wmo) ? wmo : -1
        return WeatherInfo(tempTenthsC: min(max(temp, -999), 999), wmoCode: code, city: clean(p[2], 24))
    }

    // ---- helpers

    /// Invisible and direction-changing code points, Unicode categories Cf, Zl and Zp: bidi controls and isolates,
    /// zero-width characters, U+061C, U+2028/2029, U+2060-2064, U+180E, tag characters. They could make a command on
    /// the approval sheet read differently from what runs. Mirrors DashWire.cs.
    public static func isBidi(_ scalar: Unicode.Scalar) -> Bool {
        let category = scalar.properties.generalCategory
        return category == .format || category == .lineSeparator || category == .paragraphSeparator
    }

    static func isAgent(_ agent: String) -> Bool {
        AgentToken(rawValue: agent) != nil
    }

    static func isState(_ state: String) -> Bool {
        SessionStateToken(rawValue: state) != nil
    }

    static func isControl(_ scalar: Unicode.Scalar) -> Bool {
        scalar.properties.generalCategory == .control
    }

    static func isDigit(_ scalar: Unicode.Scalar) -> Bool {
        ("0"..."9").contains(scalar)
    }

    static func fit(_ parts: [String], _ separator: String, _ maxBytes: Int) -> String {
        for count in stride(from: parts.count, to: 0, by: -1) {
            let text = parts.prefix(count).joined(separator: separator)
            if text.utf8.count <= maxBytes { return text }
        }
        return ""
    }

    /// `text` cut at every `separator` scalar, into at most `limit` parts (the last keeps the rest). "" is one part.
    static func split(_ text: String, _ separator: Unicode.Scalar, limit: Int = .max) -> [String] {
        var parts: [String] = []
        var current = String.UnicodeScalarView()
        for scalar in text.unicodeScalars {
            if scalar == separator && parts.count < limit - 1 {
                parts.append(String(current))
                current = String.UnicodeScalarView()
            } else {
                current.append(scalar)
            }
        }
        parts.append(String(current))
        return parts
    }

    /// The "|" entries of a list payload, each read by `read`, the ones it returns nil for skipped.
    static func entries<T>(_ wire: String, _ read: (String) -> T?) -> [T] {
        wire.isEmpty ? [] : split(wire, "|").compactMap(read)
    }

    /// An optional minus sign and digits, nothing else (no plus sign, no spaces), that fits an Int64.
    static func long(_ text: String) -> Int64? {
        let scalars = Array(text.unicodeScalars)
        let start = scalars.first == "-" ? 1 : 0
        guard scalars.count > start, scalars.count <= 19, scalars[start...].allSatisfy(isDigit) else { return nil }
        return Int64(text)
    }

    static func int(_ text: String) -> Int? {
        guard let wide = long(text), wide >= Int64(Int32.min), wide <= Int64(Int32.max) else { return nil }
        return Int(wide)
    }

    private static func pct(_ v: Int) -> String {
        v < 0 ? "-1" : String(min(v, 100))
    }

    private static func num<T: BinaryInteger>(_ v: T) -> String {
        v < 0 ? "-1" : String(v)
    }

    private static func big(_ v: Int64, _ whenNegative: Int64) -> String {
        String(v < 0 ? whenNegative : v)
    }
}

/// The body of a BLOB PERM_ASK: UTF-8 "agent", "tool" and "text" joined by line feeds, at most 4096 bytes.
public enum PermAskBody {
    public static let maxBytes = 4096
    static let maxToolLength = 24

    /// The body for an approval. The text is cleaned with `DashWire.cleanText` and the tool name reduced to
    /// A-Za-z0-9_.-. Complete only when nothing was cut on the way (`truncated` false), the head plus the RAW text fit
    /// 4096 bytes, and the cleaned body fits too, so cleaning can never hide a cut. Otherwise the text is left out and
    /// `complete` is false, so the phone offers Deny only.
    public static func encode(agent: String, tool: String, text: String, truncated: Bool = false) -> (
        body: String, complete: Bool
    ) {
        let head = agent + "\n" + toolName(tool) + "\n"
        let body = head + DashWire.cleanText(text)
        let complete = !truncated && head.utf8.count + text.utf8.count <= maxBytes && body.utf8.count <= maxBytes
        return (complete ? body : head, complete)
    }

    /// The agent must be known, the tool 1..24 of A-Za-z0-9_.-, and the body at most 4096 bytes. The text may hold
    /// more line feeds.
    public static func parse(_ body: String) -> PermAsk? {
        guard body.utf8.count <= maxBytes else { return nil }
        let scalars = Array(body.unicodeScalars)
        guard let first = scalars.firstIndex(of: "\n") else { return nil }
        guard let second = scalars[(first + 1)...].firstIndex(of: "\n") else { return nil }
        let agent = String(String.UnicodeScalarView(scalars[..<first]))
        let tool = scalars[(first + 1)..<second]
        guard DashWire.isAgent(agent) else { return nil }
        guard (1...maxToolLength).contains(tool.count), tool.allSatisfy(isToolChar) else { return nil }
        let text = String(String.UnicodeScalarView(scalars[(second + 1)...]))
        return PermAsk(agent: agent, tool: String(String.UnicodeScalarView(tool)), text: text)
    }

    static func isToolChar(_ scalar: Unicode.Scalar) -> Bool {
        ("a"..."z").contains(scalar) || ("A"..."Z").contains(scalar) || ("0"..."9").contains(scalar)
            || scalar == "_" || scalar == "." || scalar == "-"
    }

    private static func toolName(_ tool: String) -> String {
        let clean = tool.unicodeScalars.prefix(maxToolLength).map { isToolChar($0) ? $0 : "_" }
        return clean.isEmpty ? "unknown" : String(String.UnicodeScalarView(clean))
    }
}

/// The body of a BLOB GITHUB: lines of `section TAB repo TAB number TAB title TAB flags TAB ci` joined by line feeds,
/// at most 8 KiB.
public enum GitHubBlob {
    public static let maxBytes = 8192
    public static let maxPerSection = 10
    static let maxRepoPart = 100

    /// Lines in the order given, an invalid one dropped, at most 10 per section, the last lines dropped until it fits
    /// 8 KiB. An N line always reads `N TAB TAB count TAB TAB TAB none`.
    public static func encode(_ lines: [GitHubLine]) -> String {
        var perSection: [String: Int] = [:]
        var rows: [String] = []
        for raw in lines {
            guard let line = normalise(raw) else { continue }
            perSection[line.section, default: 0] += 1
            guard perSection[line.section, default: 0] <= maxPerSection else { continue }
            let fields = [line.section, line.repo, String(line.number), line.title, line.draft ? "d" : "", line.ci]
            rows.append(fields.joined(separator: "\t"))
        }
        return DashWire.fit(rows, "\n", maxBytes)
    }

    /// Lines that do not read, or are blank, are skipped.
    public static func parse(_ body: String) -> [GitHubLine] {
        DashWire.split(body, "\n").compactMap { row in
            let f = DashWire.split(row, "\t")
            guard f.count == 6, let number = DashWire.long(f[2]) else { return nil }
            let line = GitHubLine(section: f[0], repo: f[1], number: number, title: f[3], draft: f[4] == "d", ci: f[5])
            return normalise(line)
        }
    }

    private static func normalise(_ line: GitHubLine) -> GitHubLine? {
        guard line.number >= 0 else { return nil }
        let none = CiToken.none.rawValue
        if line.section == "N" {
            return GitHubLine(section: "N", repo: "", number: line.number, title: "", draft: false, ci: none)
        }
        guard line.section == "R" || line.section == "M", isRepo(line.repo) else { return nil }
        let ci = CiToken(rawValue: line.ci).map(\.rawValue) ?? none
        let title = DashWire.clean(line.title, 80)
        return GitHubLine(
            section: line.section, repo: line.repo, number: line.number, title: title, draft: line.draft, ci: ci)
    }

    private static func isRepo(_ repo: String) -> Bool {
        let parts = DashWire.split(repo, "/")
        return parts.count == 2 && parts.allSatisfy(isRepoPart)
    }

    private static func isRepoPart(_ part: String) -> Bool {
        let scalars = part.unicodeScalars
        return (1...maxRepoPart).contains(scalars.count) && scalars.allSatisfy(PermAskBody.isToolChar)
    }
}
