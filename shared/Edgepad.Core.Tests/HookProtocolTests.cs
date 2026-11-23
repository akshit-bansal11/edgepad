using System.Text;
using System.Text.Json.Nodes;
using Edgepad.Hooks;
using Xunit;

namespace Edgepad.Tests;

/// <summary>Holds HookProtocol to the SERVER lines of protocol/hook-vectors.txt, which the Swift suite reads too, plus the size cases.</summary>
public sealed class HookProtocolTests
{
    [Fact]
    public void EveryServerVectorParsesAsWritten()
    {
        var count = 0;
        foreach (var line in Fixtures.Lines("hook-vectors.txt").Where(l => l.StartsWith("SERVER ", StringComparison.Ordinal)))
        {
            var at = line.IndexOf(" => ", StringComparison.Ordinal);
            var input = line["SERVER ".Length..at];
            var want = line[(at + 4)..];
            var got = HookProtocol.TryParse(Encoding.UTF8.GetBytes(input));
            if (want == "NONE")
            {
                Assert.True(got is null, line);
            }
            else
            {
                Assert.True(got is not null, line);
                var expected = JsonNode.Parse(want);
                var actual = Describe(got);
                Assert.True(Same(expected, actual), $"{line}\n got {actual?.ToJsonString()}");
            }

            count++;
        }

        Assert.True(count >= 40, $"only {count} SERVER vectors");
    }

    [Theory]
    [InlineData("\"cwd\":5")]
    [InlineData("\"cwd\":\"LONG\"")]
    public void AUsageReportWithAnUnusableFolderKeepsItsLimitsAndLosesOnlyTheFolder(string cwdField)
    {
        var field = cwdField.Replace("LONG", new string('x', HookProtocol.MaxCwdLength + 1), StringComparison.Ordinal);
        var line = $$$"""{"v":1,"token":"T","agent":"claude","kind":"usage","d":{"limits":[{"kind":"five_hour","percentUsed":12}],"context":{"percent":7},{{{field}}}}}""";

        var usage = Assert.IsType<UsageRequest>(HookProtocol.TryParse(Encoding.UTF8.GetBytes(line)));

        Assert.Single(usage.Windows);
        Assert.Equal(string.Empty, usage.Cwd);
    }

    [Fact]
    public void ALineOfExactly64KiBParsesAndOneByteMoreDoesNot()
    {
        const string head = "{\"v\":1,\"token\":\"T\",\"agent\":\"claude\",\"kind\":\"session\",\"d\":{\"hook_event_name\":\"Stop\",\"pad\":\"";
        const string tail = "\"}}";
        var fill = HookProtocol.MaxLineBytes - head.Length - tail.Length;
        var exact = head + new string('x', fill) + tail;

        Assert.Equal(HookProtocol.MaxLineBytes, Encoding.UTF8.GetByteCount(exact));
        Assert.NotNull(HookProtocol.TryParse(Encoding.UTF8.GetBytes(exact)));
        Assert.Null(HookProtocol.TryParse(Encoding.UTF8.GetBytes(head + new string('x', fill + 1) + tail)));
    }

    [Theory]
    [InlineData("command")]
    [InlineData("file_path")]
    [InlineData("url")]
    public void AFieldCutAt16KIsMarkedTruncated(string field)
    {
        string Line(int n) => $$$$"""{"v":1,"token":"T","agent":"claude","kind":"perm","d":{"tool_name":"Bash","tool_input":{"{{{{field}}}}":"{{{{new string('x', n)}}}}"}}}""";

        var exact = Assert.IsType<PermRequest>(HookProtocol.TryParse(Encoding.UTF8.GetBytes(Line(HookProtocol.MaxTextLength))));
        Assert.False(exact.Input.Truncated);
        var cut = Assert.IsType<PermRequest>(HookProtocol.TryParse(Encoding.UTF8.GetBytes(Line(HookProtocol.MaxTextLength + 1))));
        Assert.True(cut.Input.Truncated);
    }

    [Fact]
    public void AnArrayCommandJoinedPast16KIsMarkedTruncated()
    {
        var part = new string('x', 1000);
        var parts = string.Join(',', Enumerable.Repeat($"\"{part}\"", 17));
        var line = $$$$"""{"v":1,"token":"T","agent":"codex","kind":"perm","d":{"tool_name":"shell","tool_input":{"command":[{{{{parts}}}}]}}}""";
        var request = Assert.IsType<PermRequest>(HookProtocol.TryParse(Encoding.UTF8.GetBytes(line)));
        Assert.True(request.Input.Truncated);
        Assert.Equal(HookProtocol.MaxTextLength, request.Input.Command!.Length);
    }

    [Theory]
    [InlineData("ls", "ls")]
    [InlineData("-la", "-la")]
    [InlineData("", "''")]
    [InlineData("a b", "'a b'")]
    [InlineData("a\tb", "'a\tb'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("say \"hi\"", "'say \"hi\"'")]
    [InlineData("$HOME;rm", "'$HOME;rm'")]
    [InlineData("a;b", "'a;b'")]
    [InlineData("a|b&c", "'a|b&c'")]
    [InlineData("*.txt", "'*.txt'")]
    [InlineData("~/x", "'~/x'")]
    [InlineData("C:\\x", "'C:\\x'")]
    [InlineData("user@host:/a,b=c+d%e_f.g-h", "user@host:/a,b=c+d%e_f.g-h")]
    [InlineData("é", "'é'")]
    public void AnArrayElementIsQuotedOnlyWhenItWouldNotReadBack(string part, string shown) => Assert.Equal(shown, HookProtocol.Quote(part));

    [Fact]
    public void TheClientCapIsAboveTheApprovalCapSoAClientCutIsNeverComplete() =>
        Assert.InRange(Dashboard.PermAskBody.MaxBytes, 0, HookClient.InputCap - 2);

    [Fact]
    public void InvalidUtf8AndAnEmptyLineAreNoRequest()
    {
        Assert.Null(HookProtocol.TryParse([]));
        Assert.Null(HookProtocol.TryParse([0x7B, 0xFF, 0xFE, 0x7D]));
    }

    [Fact]
    public void TheRepliesAreOneLineEach()
    {
        Assert.Equal("{\"v\":1,\"decision\":\"allow\"}\n", Encoding.UTF8.GetString(HookProtocol.DecisionReply(HookProtocol.Allow)));
        Assert.Equal("{\"v\":1,\"ok\":true}\n", Encoding.UTF8.GetString(HookProtocol.OkReply()));
    }

    private static JsonObject Describe(HookRequest request) => request switch
    {
        PermRequest p => new JsonObject
        {
            ["kind"] = "perm",
            ["agent"] = p.Agent,
            ["token"] = p.Token,
            ["session"] = p.Session,
            ["cwd"] = p.Cwd,
            ["tool"] = p.Tool,
            ["command"] = p.Input.Command,
            ["file_path"] = p.Input.FilePath,
            ["url"] = p.Input.Url,
            ["command_safe"] = p.Input.CommandSafe,
            ["truncated"] = p.Input.Truncated,
            ["text"] = PermissionBroker.TextFor(p),
        },
        SessionRequest s => new JsonObject
        {
            ["kind"] = "session",
            ["agent"] = s.Agent,
            ["token"] = s.Token,
            ["session"] = s.Session,
            ["event"] = s.Event,
            ["cwd"] = s.Cwd,
            ["notification_type"] = s.NotificationType,
        },
        UsageRequest u => new JsonObject
        {
            ["kind"] = "usage",
            ["agent"] = u.Agent,
            ["token"] = u.Token,
            ["windows"] = Windows(u.Windows),
            ["context"] = u.ContextPct,
        },
        SpendRequest s => new JsonObject
        {
            ["kind"] = "spend",
            ["agent"] = s.Agent,
            ["token"] = s.Token,
            ["today"] = Pair(s.Totals.TokToday, s.Totals.CentsToday),
            ["week"] = Pair(s.Totals.TokWeek, s.Totals.CentsWeek),
            ["month"] = Pair(s.Totals.TokMonth, s.Totals.CentsMonth),
        },
        _ => throw new InvalidOperationException(request.GetType().Name),
    };

    // JSON equality with key order free and numbers compared by value (40 equals 40.0).
    private static bool Same(JsonNode? a, JsonNode? b) => (a, b) switch
    {
        (null, null) => true,
        (JsonObject x, JsonObject y) => x.Count == y.Count && x.All(p => y.TryGetPropertyValue(p.Key, out var v) && Same(p.Value, v)),
        (JsonArray x, JsonArray y) => x.Count == y.Count && x.Zip(y).All(p => Same(p.First, p.Second)),
        (JsonValue x, JsonValue y) when x.GetValueKind() == System.Text.Json.JsonValueKind.Number && y.GetValueKind() == System.Text.Json.JsonValueKind.Number =>
            Number(x) == Number(y),
        (JsonValue x, JsonValue y) when x.GetValueKind() == System.Text.Json.JsonValueKind.String && y.GetValueKind() == System.Text.Json.JsonValueKind.String =>
            x.GetValue<string>() == y.GetValue<string>(),
        (JsonValue x, JsonValue y) => x.GetValueKind() == y.GetValueKind(),
        _ => false,
    };

    private static JsonArray Windows(IReadOnlyList<Dashboard.RawLimit> windows)
    {
        var array = new JsonArray();
        foreach (var w in windows)
        {
            array.Add(new JsonObject { ["window"] = w.Window, ["pct"] = w.Pct, ["resets_at"] = w.ResetsAt });
        }

        return array;
    }

    private static JsonArray Pair(long first, long second)
    {
        var array = new JsonArray();
        array.Add(JsonValue.Create(first));
        array.Add(JsonValue.Create(second));
        return array;
    }

    private static double Number(JsonValue value) => double.Parse(value.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
}
