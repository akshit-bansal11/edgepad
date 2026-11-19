using System.Globalization;
using System.Text;
using Edgepad.Dashboard;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// Holds the dashboard codecs to protocol/dashboard-vectors.txt, which the Kotlin and Swift suites read too. The file's header
/// says what each CODEC line means; this class is the C# half of that definition.
/// </summary>
public sealed class DashWireTests
{
    public static TheoryData<string> Vectors
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var line in Fixtures.Lines("dashboard-vectors.txt"))
            {
                data.Add(line);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void EveryVectorEncodesToItsWireTextAndReadsBack(string line)
    {
        var arrow = line.IndexOf(" => ", StringComparison.Ordinal);
        var head = line[..arrow];
        var split = head.IndexOf(' ', StringComparison.Ordinal);
        var codec = head[..split];
        var args = head[(split + 1)..];
        var rawExpected = line[(arrow + 4)..];

        var actual = Run(codec, args);

        if (rawExpected == "<none>")
        {
            Assert.Null(actual);
            return;
        }

        Assert.Equal(rawExpected == "<empty>" ? string.Empty : Unescape(rawExpected), actual);
        Assert.DoesNotContain(actual!, c => DashWire.IsBidi(c));
        RoundTrips(codec, actual!);
    }

    [Fact]
    public void CleanNeverSplitsASurrogatePair()
    {
        const string Faces = "a😀b😀😀c😀";
        for (var max = 0; max <= 10; max++)
        {
            var clean = DashWire.Clean(Faces, max);
            Assert.True(clean.EnumerateRunes().Count() <= max);
            for (var i = 0; i < clean.Length; i++)
            {
                if (char.IsHighSurrogate(clean[i]))
                {
                    Assert.True(i + 1 < clean.Length && char.IsLowSurrogate(clean[i + 1]));
                    i++;
                }
                else
                {
                    Assert.False(char.IsLowSurrogate(clean[i]));
                }
            }
        }
    }

    [Fact]
    public void EveryTextPayloadFitsTheFrame()
    {
        var wide = string.Concat(Enumerable.Repeat("😀", 60));
        var payloads = new[]
        {
            DashWire.EncodeStatsInfo(new StatsInfo(wide, int.MaxValue, wide, int.MaxValue, wide)),
            DashWire.EncodeDisks([.. Enumerable.Repeat(new DiskEntry(wide, 100, long.MaxValue, long.MaxValue), 9)]),
            DashWire.EncodeSessions([.. Enumerable.Repeat(new SessionEntry("codex", "wait", wide), 20)]),
            DashWire.EncodeLimits([.. Enumerable.Repeat(new LimitEntry("codex", "~" + wide, 100, long.MaxValue, long.MaxValue), 20)]),
            DashWire.EncodeSpend([.. Enumerable.Repeat(new SpendEntry("codex", long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue), 5)]),
            DashWire.EncodeWeather(new WeatherInfo(int.MaxValue, int.MaxValue, wide)),
        };

        foreach (var payload in payloads)
        {
            Assert.InRange(Encoding.UTF8.GetByteCount(payload), 1, 255);
        }
    }

    [Fact]
    public void TheGitHubBlobKeepsWholeLinesWithinEightKiB()
    {
        var repo = new string('r', 100) + "/" + new string('r', 100);
        var title = string.Concat(Enumerable.Repeat("😀", 80));
        var lines = new List<GitHubLine>();
        for (var i = 0; i < 12; i++)
        {
            lines.Add(new GitHubLine("R", repo, 12, title, false, "ok"));
        }

        for (var i = 0; i < 12; i++)
        {
            lines.Add(new GitHubLine("M", repo, 12, title, false, "ok"));
        }

        var blob = GitHubBlob.Encode(lines);

        var rows = blob.Split('\n');
        Assert.InRange(Encoding.UTF8.GetByteCount(blob), 1, GitHubBlob.MaxBytes);
        Assert.Equal(15, rows.Length);
        Assert.All(rows, row => Assert.Equal(531, Encoding.UTF8.GetByteCount(row)));
        Assert.Equal(10, rows.Count(row => row.StartsWith("R\t", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheSeparatorsAreTheRealCharacters()
    {
        Assert.Equal("claude\nBash\nx", PermAskBody.Encode("claude", "Bash", "x", out var complete));
        Assert.True(complete);
        Assert.Equal("R\ta/b\t1\tt\t\tok", GitHubBlob.Encode([new GitHubLine("R", "a/b", 1, "t", false, "ok")]));
    }

    private static string? Run(string codec, string args)
    {
        switch (codec)
        {
            case "CLEAN":
                {
                    var space = args.IndexOf(' ', StringComparison.Ordinal);
                    return DashWire.Clean(Unescape(args[(space + 1)..]), int.Parse(args[..space], CultureInfo.InvariantCulture));
                }

            case "CLEANTEXT":
                return DashWire.CleanText(Unescape(args));
            case "STATS":
                {
                    var n = args.Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                    return DashWire.EncodeStats(new StatsSample(n[0], n[1], n[2], n[3], n[4], n[5], n[6], n[7], n[8], n[9], n[10]));
                }

            case "STATSPARSE":
                return DashWire.TryParseStats(Field(args), out var stats) ? DashWire.EncodeStats(stats) : null;
            case "STATSINFO":
                {
                    var f = args.Split(" ;; ");
                    return DashWire.EncodeStatsInfo(new StatsInfo(Field(f[0]), Int(f[1]), Field(f[2]), Int(f[3]), Field(f[4])));
                }

            case "STATSINFOPARSE":
                return DashWire.TryParseStatsInfo(Field(args), out var info) ? DashWire.EncodeStatsInfo(info) : null;
            case "DISKS":
                return DashWire.EncodeDisks([.. Entries(args).Select(e => Fields(e, 4)).Select(f => new DiskEntry(Field(f[0]), Int(f[1]), Long(f[2]), Long(f[3])))]);
            case "DISKSPARSE":
                return DashWire.EncodeDisks([.. DashWire.ParseDisks(Field(args))]);
            case "SESSIONS":
                return DashWire.EncodeSessions([.. Entries(args).Select(e => Fields(e, 3)).Select(f => new SessionEntry(Field(f[0]), Field(f[1]), Field(f[2])))]);
            case "SESSIONSPARSE":
                return DashWire.EncodeSessions([.. DashWire.ParseSessions(Field(args))]);
            case "PERMANSWER":
                return PermAnswer.TryParse(Field(args), out var answer) ? (answer.Allow ? "allow " : "deny ") + answer.Id : null;
            case "PERMLABEL":
                {
                    var f = args.Split(' ');
                    return DashWire.PermLabel(f[0], f[1] == "1");
                }

            case "PERMLABELPARSE":
                return DashWire.TryParsePermLabel(Field(args), out var id, out var complete) ? id + " " + (complete ? "1" : "0") : null;
            case "PERMASK":
                {
                    var f = Fields(args, 3);
                    var body = PermAskBody.Encode(Field(f[0]), Field(f[1]), Field(f[2]), out var whole);
                    return (whole ? "1 " : "0 ") + body;
                }

            case "PERMASKPARSE":
                return PermAskBody.TryParse(Field(args), out var ask) ? $"{ask.Agent} @ {ask.Tool} @ {(ask.Text.Length == 0 ? "<empty>" : ask.Text)}" : null;
            case "LIMITS":
                return DashWire.EncodeLimits([.. Entries(args).Select(e => Fields(e, 5)).Select(f => new LimitEntry(Field(f[0]), Field(f[1]), double.Parse(Field(f[2]), CultureInfo.InvariantCulture), Long(f[3]), Long(f[4])))]);
            case "LIMITSPARSE":
                return DashWire.EncodeLimits([.. DashWire.ParseLimits(Field(args))]);
            case "SPEND":
                return DashWire.EncodeSpend([.. Entries(args).Select(e => Fields(e, 9)).Select(f => new SpendEntry(Field(f[0]), Long(f[1]), Long(f[2]), Long(f[3]), Long(f[4]), Long(f[5]), Long(f[6]), Long(f[7]), Field(f[8]) == "1"))]);
            case "SPENDPARSE":
                return DashWire.EncodeSpend([.. DashWire.ParseSpend(Field(args))]);
            case "WEATHER":
                {
                    if (args == "none")
                    {
                        return DashWire.EncodeWeather(null);
                    }

                    var f = Fields(args, 3);
                    return DashWire.EncodeWeather(new WeatherInfo(Int(f[0]), Int(f[1]), Field(f[2])));
                }

            case "WEATHERPARSE":
                return DashWire.TryParseWeather(Field(args), out var weather) ? DashWire.EncodeWeather(weather) : null;
            case "GITHUB":
                return GitHubBlob.Encode([.. Entries(args).Select(e => Fields(e, 6)).Select(f => new GitHubLine(Field(f[0]), Field(f[1]), Long(f[2]), Field(f[3]), Field(f[4]) == "d", Field(f[5])))]);
            case "GITHUBPARSE":
                return GitHubBlob.Encode([.. GitHubBlob.Parse(Field(args))]);
            default:
                throw new InvalidOperationException($"unknown codec {codec} in protocol/dashboard-vectors.txt");
        }
    }

    // What a codec wrote must read back and write the same bytes again.
    private static void RoundTrips(string codec, string wire)
    {
        switch (codec)
        {
            case "STATS":
                Assert.True(DashWire.TryParseStats(wire, out var stats));
                Assert.Equal(wire, DashWire.EncodeStats(stats));
                Assert.InRange(Encoding.UTF8.GetByteCount(wire), 1, 255);
                break;
            case "STATSINFO":
                Assert.True(DashWire.TryParseStatsInfo(wire, out var info));
                Assert.Equal(wire, DashWire.EncodeStatsInfo(info));
                Assert.InRange(Encoding.UTF8.GetByteCount(wire), 1, 255);
                break;
            case "DISKS":
                Assert.Equal(wire, DashWire.EncodeDisks([.. DashWire.ParseDisks(wire)]));
                Assert.InRange(Encoding.UTF8.GetByteCount(wire), 0, 255);
                break;
            case "SESSIONS":
                Assert.Equal(wire, DashWire.EncodeSessions([.. DashWire.ParseSessions(wire)]));
                Assert.InRange(Encoding.UTF8.GetByteCount(wire), 0, 255);
                break;
            case "LIMITS":
                Assert.Equal(wire, DashWire.EncodeLimits([.. DashWire.ParseLimits(wire)]));
                Assert.InRange(Encoding.UTF8.GetByteCount(wire), 0, 255);
                break;
            case "SPEND":
                Assert.Equal(wire, DashWire.EncodeSpend([.. DashWire.ParseSpend(wire)]));
                Assert.InRange(Encoding.UTF8.GetByteCount(wire), 0, 255);
                break;
            case "WEATHER" when wire.Length > 0:
                Assert.True(DashWire.TryParseWeather(wire, out var weather));
                Assert.Equal(wire, DashWire.EncodeWeather(weather));
                Assert.InRange(Encoding.UTF8.GetByteCount(wire), 1, 255);
                break;
            case "GITHUB":
                Assert.Equal(wire, GitHubBlob.Encode([.. GitHubBlob.Parse(wire)]));
                Assert.InRange(Encoding.UTF8.GetByteCount(wire), 0, GitHubBlob.MaxBytes);
                break;
            case "PERMASK" when wire.StartsWith("1 ", StringComparison.Ordinal):
                Assert.True(PermAskBody.TryParse(wire[2..], out _));
                break;
            default:
                break;
        }
    }

    private static string Field(string field) => field == "<empty>" ? string.Empty : Unescape(field);

    private static int Int(string field) => int.Parse(Field(field), CultureInfo.InvariantCulture);

    private static long Long(string field) => long.Parse(Field(field), CultureInfo.InvariantCulture);

    private static string[] Entries(string args) => args == "<empty>" ? [] : args.Split(" ;; ");

    private static string[] Fields(string entry, int count) => entry.Split(" @ ", count, StringSplitOptions.None);

    // \t \n \\ \u{HEX} and \{N*c}: the escapes the vector file's header defines.
    private static string Unescape(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\')
            {
                sb.Append(text[i]);
                continue;
            }

            switch (text[++i])
            {
                case 't':
                    sb.Append('\t');
                    break;
                case 'n':
                    sb.Append('\n');
                    break;
                case '\\':
                    sb.Append('\\');
                    break;
                case 'u':
                    {
                        var close = text.IndexOf('}', i);
                        var hex = text.AsSpan(i + 2, close - i - 2);
                        sb.Append(char.ConvertFromUtf32(int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                        i = close;
                        break;
                    }

                case '{':
                    {
                        var star = text.IndexOf('*', i);
                        var end = text.IndexOf('}', star);
                        var times = int.Parse(text.AsSpan(i + 1, star - i - 1), CultureInfo.InvariantCulture);
                        sb.Insert(sb.Length, text[(star + 1)..end], times);
                        i = end;
                        break;
                    }

                default:
                    throw new FormatException($"unknown escape in protocol/dashboard-vectors.txt: {text}");
            }
        }

        return sb.ToString();
    }
}
