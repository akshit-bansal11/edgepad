import XCTest

@testable import EdgepadCore

/// Holds the dashboard codecs to protocol/dashboard-vectors.txt, which the C# and Kotlin suites read too. The file's
/// header says what each CODEC line means; this class is the Swift half of that definition.
final class DashWireTests: XCTestCase {
    func testEveryVectorEncodesToItsWireTextAndReadsBack() throws {
        let lines = try Fixtures.lines("dashboard-vectors.txt")
        XCTAssertGreaterThan(lines.count, 100)
        for line in lines {
            let arrow = try XCTUnwrap(line.range(of: " => "), line)
            let head = String(line[..<arrow.lowerBound])
            let rawExpected = String(line[arrow.upperBound...])
            let space = try XCTUnwrap(head.firstIndex(of: " "), line)
            let codec = String(head[..<space])
            let args = String(head[head.index(after: space)...])

            let actual = run(codec, args)

            if rawExpected == "<none>" {
                XCTAssertNil(actual, line)
                continue
            }
            let wire = try XCTUnwrap(actual, line)
            XCTAssertEqual(Array(wire.unicodeScalars), Array(field(rawExpected).unicodeScalars), line)
            XCTAssertFalse(wire.unicodeScalars.contains(where: DashWire.isBidi), line)
            roundTrips(codec, wire, line)
        }
    }

    func testCleanCountsCodePoints() {
        let faces = "a😀b😀😀c😀"
        for limit in 0...10 {
            let clean = DashWire.clean(faces, limit)
            XCTAssertLessThanOrEqual(clean.unicodeScalars.count, limit)
            XCTAssertTrue(faces.unicodeScalars.starts(with: clean.unicodeScalars))
        }
    }

    func testEveryTextPayloadFitsTheFrame() {
        let wide = String(repeating: "😀", count: 60)
        let big = Int64.max
        let spend = SpendEntry(
            agent: "codex", measuredAtMin: big, tokToday: big, centsToday: big, tokWeek: big, centsWeek: big,
            tokMonth: big, centsMonth: big)
        let most = Int(Int32.max)
        let info = StatsInfo(cpuName: wide, ramMiB: most, gpuName: wide, vramMiB: most, netName: wide)
        let session = SessionEntry(agent: "codex", state: "wait", title: wide)
        let disk = DiskEntry(label: wide, usedPct: 100, totalGiB: big, freeGiB: big)
        let limit = LimitEntry(agent: "codex", window: "~" + wide, pct: 100, resetAtMin: big, measuredAtMin: big)
        let payloads = [
            DashWire.encodeStatsInfo(info),
            DashWire.encodeDisks(Array(repeating: disk, count: 9)),
            DashWire.encodeSessions(Array(repeating: session, count: 20)),
            DashWire.encodeLimits(Array(repeating: limit, count: 20)),
            DashWire.encodeSpend(Array(repeating: spend, count: 5)),
            DashWire.encodeWeather(WeatherInfo(tempTenthsC: most, wmoCode: most, city: wide)),
        ]
        for payload in payloads {
            XCTAssertTrue((1...255).contains(payload.utf8.count), payload)
        }
    }

    func testTheGitHubBlobKeepsWholeLinesWithinEightKiB() {
        let repo = String(repeating: "r", count: 100) + "/" + String(repeating: "r", count: 100)
        let title = String(repeating: "😀", count: 80)
        let mine = GitHubLine(section: "M", repo: repo, number: 12, title: title, draft: false, ci: "ok")
        let review = GitHubLine(section: "R", repo: repo, number: 12, title: title, draft: false, ci: "ok")
        let blob = GitHubBlob.encode(Array(repeating: review, count: 12) + Array(repeating: mine, count: 12))

        let rows = blob.components(separatedBy: "\n")
        XCTAssertTrue((1...GitHubBlob.maxBytes).contains(blob.utf8.count))
        XCTAssertEqual(rows.count, 15)
        XCTAssertTrue(rows.allSatisfy { $0.utf8.count == 531 })
        XCTAssertEqual(rows.filter { $0.hasPrefix("R\t") }.count, 10)
    }

    func testTheSeparatorsAreTheRealCharacters() {
        let ask = PermAskBody.encode(agent: "claude", tool: "Bash", text: "x")
        XCTAssertEqual(ask.body.utf8.filter { $0 == 0x0A }.count, 2)
        XCTAssertEqual(ask.body.replacingOccurrences(of: "\n", with: "|"), "claude|Bash|x")
        XCTAssertTrue(ask.complete)
        let line = GitHubLine(section: "R", repo: "a/b", number: 1, title: "t", draft: false, ci: "ok")
        let blob = GitHubBlob.encode([line])
        XCTAssertEqual(blob.utf8.filter { $0 == 0x09 }.count, 5)
        XCTAssertEqual(blob.replacingOccurrences(of: "\t", with: "|"), "R|a/b|1|t||ok")
    }

    // ---- the codecs the vector file names

    private func run(_ codec: String, _ args: String) -> String? {
        switch codec {
        case "CLEAN":
            let space = args.firstIndex(of: " ") ?? args.endIndex
            let text = args[space...].dropFirst()
            return DashWire.clean(unescape(String(text)), Int(args[..<space]) ?? -1)
        case "CLEANTEXT":
            return DashWire.cleanText(unescape(args))
        case "STATS":
            // The wire order is the record's order, so the parser builds the record from the same eleven numbers.
            return DashWire.parseStats(args.replacingOccurrences(of: ",", with: "/")).map(DashWire.encodeStats)
        case "STATSPARSE":
            return DashWire.parseStats(field(args)).map(DashWire.encodeStats)
        case "STATSINFO":
            let f = args.components(separatedBy: " ;; ")
            let info = StatsInfo(
                cpuName: field(f[0]), ramMiB: int(f[1]), gpuName: field(f[2]), vramMiB: int(f[3]), netName: field(f[4]))
            return DashWire.encodeStatsInfo(info)
        case "STATSINFOPARSE":
            return DashWire.parseStatsInfo(field(args)).map(DashWire.encodeStatsInfo)
        case "DISKS":
            let disks = entries(args).map { fields($0, 4) }.map { f in
                DiskEntry(label: field(f[0]), usedPct: int(f[1]), totalGiB: long(f[2]), freeGiB: long(f[3]))
            }
            return DashWire.encodeDisks(disks)
        case "DISKSPARSE":
            return DashWire.encodeDisks(DashWire.parseDisks(field(args)))
        case "SESSIONS":
            let sessions = entries(args).map { fields($0, 3) }.map { f in
                SessionEntry(agent: field(f[0]), state: field(f[1]), title: field(f[2]))
            }
            return DashWire.encodeSessions(sessions)
        case "SESSIONSPARSE":
            return DashWire.encodeSessions(DashWire.parseSessions(field(args)))
        case "PERMANSWER":
            return PermAnswer.parse(field(args)).map { ($0.allow ? "allow " : "deny ") + $0.id }
        case "PERMLABEL":
            let f = args.components(separatedBy: " ")
            return DashWire.permLabel(f[0], complete: f[1] == "1")
        case "PERMLABELPARSE":
            return DashWire.parsePermLabel(field(args)).map { $0.id + " " + ($0.complete ? "1" : "0") }
        case "PERMASK":
            let f = fields(args, 3)
            let ask = PermAskBody.encode(agent: field(f[0]), tool: field(f[1]), text: field(f[2]))
            return (ask.complete ? "1 " : "0 ") + ask.body
        case "PERMASKPARSE":
            guard let ask = PermAskBody.parse(field(args)) else { return nil }
            return "\(ask.agent) @ \(ask.tool) @ \(ask.text.isEmpty ? "<empty>" : ask.text)"
        default:
            return runLists(codec, args)
        }
    }

    private func runLists(_ codec: String, _ args: String) -> String? {
        switch codec {
        case "LIMITS":
            let limits = entries(args).map { fields($0, 5) }.map { f in
                let pct = Double(field(f[2])) ?? .nan
                return LimitEntry(
                    agent: field(f[0]),
                    window: field(f[1]),
                    pct: pct,
                    resetAtMin: long(f[3]),
                    measuredAtMin: long(f[4]))
            }
            return DashWire.encodeLimits(limits)
        case "LIMITSPARSE":
            return DashWire.encodeLimits(DashWire.parseLimits(field(args)))
        case "SPEND":
            let spend = entries(args).map { fields($0, 9) }.map { f in
                SpendEntry(
                    agent: field(f[0]), measuredAtMin: long(f[1]), tokToday: long(f[2]), centsToday: long(f[3]),
                    tokWeek: long(f[4]), centsWeek: long(f[5]), tokMonth: long(f[6]), centsMonth: long(f[7]),
                    unpriced: field(f[8]) == "1")
            }
            return DashWire.encodeSpend(spend)
        case "SPENDPARSE":
            return DashWire.encodeSpend(DashWire.parseSpend(field(args)))
        case "WEATHER":
            if args == "none" { return DashWire.encodeWeather(nil) }
            let f = fields(args, 3)
            return DashWire.encodeWeather(WeatherInfo(tempTenthsC: int(f[0]), wmoCode: int(f[1]), city: field(f[2])))
        case "WEATHERPARSE":
            return DashWire.parseWeather(field(args)).map { DashWire.encodeWeather($0) }
        case "GITHUB":
            let lines = entries(args).map { fields($0, 6) }.map { f in
                GitHubLine(
                    section: field(f[0]), repo: field(f[1]), number: long(f[2]), title: field(f[3]),
                    draft: field(f[4]) == "d", ci: field(f[5]))
            }
            return GitHubBlob.encode(lines)
        case "GITHUBPARSE":
            return GitHubBlob.encode(GitHubBlob.parse(field(args)))
        default:
            XCTFail("unknown codec \(codec) in protocol/dashboard-vectors.txt")
            return nil
        }
    }

    /// What a codec wrote must read back and write the same bytes again.
    private func roundTrips(_ codec: String, _ wire: String, _ line: String) {
        let bytes = wire.utf8.count
        switch codec {
        case "STATS":
            XCTAssertEqual(DashWire.parseStats(wire).map(DashWire.encodeStats), wire, line)
            XCTAssertTrue((1...255).contains(bytes), line)
        case "STATSINFO":
            XCTAssertEqual(DashWire.parseStatsInfo(wire).map(DashWire.encodeStatsInfo), wire, line)
            XCTAssertTrue((1...255).contains(bytes), line)
        case "DISKS":
            XCTAssertEqual(DashWire.encodeDisks(DashWire.parseDisks(wire)), wire, line)
            XCTAssertTrue(bytes <= 255, line)
        case "SESSIONS":
            XCTAssertEqual(DashWire.encodeSessions(DashWire.parseSessions(wire)), wire, line)
            XCTAssertTrue(bytes <= 255, line)
        case "LIMITS":
            XCTAssertEqual(DashWire.encodeLimits(DashWire.parseLimits(wire)), wire, line)
            XCTAssertTrue(bytes <= 255, line)
        case "SPEND":
            XCTAssertEqual(DashWire.encodeSpend(DashWire.parseSpend(wire)), wire, line)
            XCTAssertTrue(bytes <= 255, line)
        case "WEATHER" where !wire.isEmpty:
            XCTAssertEqual(DashWire.parseWeather(wire).map { DashWire.encodeWeather($0) }, wire, line)
            XCTAssertTrue((1...255).contains(bytes), line)
        case "GITHUB":
            XCTAssertEqual(GitHubBlob.encode(GitHubBlob.parse(wire)), wire, line)
            XCTAssertTrue(bytes <= GitHubBlob.maxBytes, line)
        case "PERMASK" where wire.hasPrefix("1 "):
            XCTAssertNotNil(PermAskBody.parse(String(wire.dropFirst(2))), line)
        default:
            break
        }
    }

    // ---- the vector file's notation

    private func field(_ text: String) -> String {
        text == "<empty>" ? "" : unescape(text)
    }

    private func int(_ text: String) -> Int {
        Int(field(text)) ?? 0
    }

    private func long(_ text: String) -> Int64 {
        Int64(field(text)) ?? 0
    }

    private func entries(_ args: String) -> [String] {
        args == "<empty>" ? [] : args.components(separatedBy: " ;; ")
    }

    /// Cut at " @ " into at most `count` fields, the last keeping the rest.
    private func fields(_ entry: String, _ count: Int) -> [String] {
        var parts: [String] = []
        var rest = Substring(entry)
        while parts.count < count - 1, let at = rest.range(of: " @ ") {
            parts.append(String(rest[..<at.lowerBound]))
            rest = rest[at.upperBound...]
        }
        parts.append(String(rest))
        return parts
    }

    /// \t \n \\ \u{HEX} and \{N*c}: the escapes the vector file's header defines.
    private func unescape(_ text: String) -> String {
        let scalars = Array(text.unicodeScalars)
        var out = String.UnicodeScalarView()
        var i = 0
        while i < scalars.count {
            let scalar = scalars[i]
            i += 1
            guard scalar == "\\", i < scalars.count else {
                out.append(scalar)
                continue
            }
            let kind = scalars[i]
            i += 1
            switch kind {
            case "t":
                out.append("\t")
            case "n":
                out.append("\n")
            case "\\":
                out.append("\\")
            case "u":
                let close = scalars[i...].firstIndex(of: "}") ?? scalars.count
                let hex = String(String.UnicodeScalarView(scalars[(i + 1)..<close]))
                if let value = UInt32(hex, radix: 16), let code = Unicode.Scalar(value) { out.append(code) }
                i = close + 1
            case "{":
                let star = scalars[i...].firstIndex(of: "*") ?? scalars.count
                let close = scalars[star...].firstIndex(of: "}") ?? scalars.count
                let times = Int(String(String.UnicodeScalarView(scalars[i..<star]))) ?? 0
                for _ in 0..<times { out.append(contentsOf: scalars[(star + 1)..<close]) }
                i = close + 1
            default:
                XCTFail("unknown escape in protocol/dashboard-vectors.txt: \(text)")
            }
        }
        return String(out)
    }
}
