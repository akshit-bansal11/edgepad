using System.Text;
using Edgepad.Dashboard;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

/// <summary>Builders for rollout lines, in the shape of the P1 fixture, and the fixture file itself.</summary>
internal static class RolloutText
{
    public const string Meta = """{"timestamp":"x","ordinal":0,"type":"session_meta","payload":{"cwd":"/work/demo","cli_version":"0.147.0"}}""";
    public const string Started = """{"timestamp":"x","ordinal":1,"type":"event_msg","payload":{"type":"task_started","turn_id":"0"}}""";
    public const string Complete = """{"timestamp":"x","ordinal":9,"type":"event_msg","payload":{"type":"task_complete","turn_id":"0","last_agent_message":"x"}}""";
    public const string Aborted = """{"timestamp":"x","ordinal":9,"type":"event_msg","payload":{"type":"turn_aborted","turn_id":"0"}}""";

    public static string Context(string model) =>
        $$$"""{"timestamp":"x","ordinal":5,"type":"turn_context","payload":{"turn_id":"0","model":"{{{model}}}","effort":"low"}}""";

    /// <summary>A token_count line; <paramref name="rateLimits"/> is the JSON of rate_limits, "null" included.</summary>
    public static string Count(string rateLimits, long input = 0, long cached = 0, long output = 0) =>
        $$$"""{"timestamp":"x","ordinal":6,"type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":{{{input}}},"cached_input_tokens":{{{cached}}},"cache_write_input_tokens":0,"output_tokens":{{{output}}}}},"rate_limits":{{{rateLimits}}}}}""";

    /// <summary>The JSON of a rate_limits object with a primary window and an optional secondary one.</summary>
    public static string Limits(string id, string pct, int minutes, long resetsAt, string secondary = "null") =>
        $$$"""{"limit_id":"{{{id}}}","primary":{"used_percent":{{{pct}}},"window_minutes":{{{minutes}}},"resets_at":{{{resetsAt}}}},"secondary":{{{secondary}}}}""";

    public static string Join(params string[] lines) => string.Join('\n', lines) + "\n";

    public static string Fixture()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "shared", "Edgepad.Core.Tests", "Fixtures", "codex-rollout.jsonl");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException("Fixtures/codex-rollout.jsonl was not found above the test output directory");
    }

    public static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));
}

public sealed class CodexRolloutTests
{
    [Fact]
    public void TheFixtureReadsAsTheP1FactsSay()
    {
        var tail = CodexRollout.ParseTail(RolloutText.Stream(RolloutText.Fixture()));
        var totals = CodexRollout.Totals(RolloutText.Stream(RolloutText.Fixture()));

        Assert.Equal(SessionStateToken.Idle, tail.State);
        Assert.Equal(new RolloutWindow(43200, 12, 1787897318), Assert.Single(tail.Limits));
        Assert.Equal("/work/demo", tail.Cwd);
        Assert.Equal("0.147.0", tail.CliVersion);
        Assert.Equal("gpt-5.6-luna", totals.Model);
        Assert.Equal(501674 - 461056 + 7280, totals.Tokens);
        Assert.Equal(47898, totals.Tokens);
        Assert.True(OpenAiPrices.TryGet(totals.Model, out var price));
        Assert.Equal(3, OpenAiPrices.Cents(price, totals));
    }

    [Theory]
    [InlineData(48000, 200000, 24)]
    [InlineData(300000, 200000, 100)]
    [InlineData(1000, 0, -1)]
    public void TheContextFillIsTheLastRequestOverTheWindow(long last, long window, int expected)
    {
        var line = $$"""{"timestamp":"x","ordinal":6,"type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1,"output_tokens":1},"last_token_usage":{"total_tokens":{{last}}},"model_context_window":{{window}}},"rate_limits":null}}""";

        Assert.Equal(expected, CodexRollout.ParseTail(RolloutText.Stream(RolloutText.Join(RolloutText.Meta, line))).ContextPct);
    }

    [Fact]
    public void ATokenCountWithNoInfoLeavesTheContextUnknown()
    {
        Assert.Equal(-1, CodexRollout.ParseTail(RolloutText.Stream(RolloutText.Join(RolloutText.Meta, RolloutText.Count("null")))).ContextPct);
    }

    [Fact]
    public void AnUnmatchedTaskStartedIsWorkingAndAbortedIsIdle()
    {
        Assert.Equal(SessionStateToken.Busy, CodexRollout.ParseTail(RolloutText.Stream(RolloutText.Join(RolloutText.Meta, RolloutText.Started))).State);
        Assert.Equal(SessionStateToken.Idle, CodexRollout.ParseTail(RolloutText.Stream(RolloutText.Join(RolloutText.Started, RolloutText.Aborted))).State);
        Assert.Equal(SessionStateToken.Busy, CodexRollout.ParseTail(RolloutText.Stream(RolloutText.Join(RolloutText.Started, RolloutText.Complete, RolloutText.Started))).State);
        Assert.Null(CodexRollout.ParseTail(RolloutText.Stream(string.Empty)).State);
    }

    [Fact]
    public void NullRateLimitsAndANullPrimaryGiveNoLimits()
    {
        var nullLimits = RolloutText.Join(RolloutText.Count("null"));
        var nullPrimary = RolloutText.Join(RolloutText.Count("""{"limit_id":"premium","primary":null,"secondary":null}"""));

        Assert.Empty(CodexRollout.ParseTail(RolloutText.Stream(nullLimits)).Limits);
        Assert.Empty(CodexRollout.ParseTail(RolloutText.Stream(nullPrimary)).Limits);
    }

    [Fact]
    public void ANullLaterLineDoesNotEraseTheLimitsAlreadySeen()
    {
        var text = RolloutText.Join(RolloutText.Count(RolloutText.Limits("codex", "40", 10080, 1787897318)), RolloutText.Count("null"));

        Assert.Equal(40, Assert.Single(CodexRollout.ParseTail(RolloutText.Stream(text)).Limits).Pct);
    }

    [Fact]
    public void TheCodexLimitIsPreferredAndASecondaryWindowIsKept()
    {
        var secondary = """{"used_percent":3.5,"window_minutes":300,"resets_at":1787000000}""";
        var text = RolloutText.Join(
            RolloutText.Count(RolloutText.Limits("codex", "10", 10080, 1787897318, secondary)),
            RolloutText.Count(RolloutText.Limits("premium", "99", 43200, 1787897999)));

        var limits = CodexRollout.ParseTail(RolloutText.Stream(text)).Limits;

        Assert.Equal([new RolloutWindow(10080, 10, 1787897318), new RolloutWindow(300, 4, 1787000000)], limits.ToArray());
    }

    [Fact]
    public void WindowsOutsideTheRangeOrMissingFieldsAreRefused()
    {
        foreach (var bad in new[]
        {
            RolloutText.Limits("codex", "10", 0, 1),
            RolloutText.Limits("codex", "10", 600000, 1),
            """{"limit_id":"codex","primary":{"window_minutes":300},"secondary":null}""",
            """{"limit_id":"codex","primary":{"used_percent":"high","window_minutes":300},"secondary":null}""",
        })
        {
            Assert.Empty(CodexRollout.ParseTail(RolloutText.Stream(RolloutText.Join(RolloutText.Count(bad)))).Limits);
        }
    }

    [Fact]
    public void APercentageIsRoundedAndClamped()
    {
        var text = RolloutText.Join(RolloutText.Count(RolloutText.Limits("codex", "250.4", 300, 1)));

        Assert.Equal(100, Assert.Single(CodexRollout.ParseTail(RolloutText.Stream(text)).Limits).Pct);
        Assert.Equal(13, Assert.Single(CodexRollout.ParseTail(RolloutText.Stream(RolloutText.Join(RolloutText.Count(RolloutText.Limits("codex", "12.6", 300, 1))))).Limits).Pct);
    }

    [Fact]
    public void ATruncatedLastLineIsSkippedAndTheRestStands()
    {
        var text = RolloutText.Join(RolloutText.Count(RolloutText.Limits("codex", "40", 10080, 1)), RolloutText.Started)
            + RolloutText.Count(RolloutText.Limits("codex", "77", 10080, 1))[..60];

        var tail = CodexRollout.ParseTail(RolloutText.Stream(text));

        Assert.Equal(40, Assert.Single(tail.Limits).Pct);
        Assert.Equal(SessionStateToken.Busy, tail.State);
    }

    [Fact]
    public void GarbageLinesAreSkippedOneByOne()
    {
        var text = RolloutText.Join(
            "not json at all event_msg",
            "{\"type\":\"event_msg\",\"payload\":5}",
            "[\"event_msg\"]",
            new string('[', 40) + "\"event_msg\"" + new string(']', 40),
            RolloutText.Started);

        Assert.Equal(SessionStateToken.Busy, CodexRollout.ParseTail(RolloutText.Stream(text)).State);
    }

    [Fact]
    public void ABigFileIsReadOnlyAtItsHeadAndItsTail()
    {
        var filler = """{"timestamp":"x","type":"response_item","payload":{"type":"function_call_output","output":"%PAD%"}}""".Replace("%PAD%", new string('a', 900), StringComparison.Ordinal);
        var text = new StringBuilder(RolloutText.Meta).Append('\n');
        text.Append(RolloutText.Count(RolloutText.Limits("codex", "5", 300, 1))).Append('\n');
        while (text.Length < 5 * 1024 * 1024)
        {
            text.Append(filler).Append('\n');
        }

        text.Append(RolloutText.Join(RolloutText.Started, RolloutText.Count(RolloutText.Limits("codex", "61", 10080, 7))));
        var counting = new Counting(Encoding.UTF8.GetBytes(text.ToString()));

        var tail = CodexRollout.ParseTail(counting);

        Assert.Equal(61, Assert.Single(tail.Limits).Pct);
        Assert.Equal(SessionStateToken.Busy, tail.State);
        Assert.Equal("/work/demo", tail.Cwd);
        Assert.True(counting.BytesRead <= CodexRollout.TailBytes + CodexRollout.HeadBytes + 32 * 1024, $"read {counting.BytesRead} bytes");
    }

    [Fact]
    public void AnOverlongLineIsSkippedWithoutBeingHeld()
    {
        var huge = """{"type":"response_item","payload":{"output":"%PAD%"}}""".Replace("%PAD%", new string('b', 2 * 1024 * 1024), StringComparison.Ordinal);
        var text = RolloutText.Join(RolloutText.Context("gpt-5.5"), huge, RolloutText.Count("null", 1000, 400, 50));

        var totals = CodexRollout.Totals(RolloutText.Stream(text));

        Assert.Equal(new RolloutTotals("gpt-5.5", 1000, 400, 50), totals);
        Assert.Equal(650, totals.Tokens);
    }

    [Fact]
    public void TheLastCumulativeUsageAndTheLastModelWin()
    {
        var text = RolloutText.Join(
            RolloutText.Context("gpt-5.4"),
            RolloutText.Count("null", 100, 10, 5),
            RolloutText.Context("gpt-5.5"),
            RolloutText.Count("null", 300, 100, 20));

        Assert.Equal(new RolloutTotals("gpt-5.5", 300, 100, 20), CodexRollout.Totals(RolloutText.Stream(text)));
    }

    [Fact]
    public void ANegativeOrOddCountIsZeroAndCachedAboveInputNeverMakesNegativeTokens()
    {
        var text = RolloutText.Join(RolloutText.Count("null", -5, 0, 7));
        Assert.Equal(7, CodexRollout.Totals(RolloutText.Stream(text)).Tokens);
        Assert.Equal(7, new RolloutTotals("m", 10, 50, 7).Tokens);
    }

    [Fact]
    public void ALongFileIsReadFromItsEndWhenTheModelIsKnown()
    {
        var filler = string.Concat(Enumerable.Repeat("""{"type":"response_item","payload":{"output":"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"}}""" + "\n", 40_000));
        var text = RolloutText.Join(RolloutText.Context("gpt-5.4"), RolloutText.Count("null", 100, 10, 5)) + filler + RolloutText.Join(RolloutText.Count("null", 300, 100, 20));
        Assert.True(text.Length > CodexRollout.TotalsTailBytes);

        // The model given back is the known one: only the end was read, where no turn_context is.
        Assert.Equal(new RolloutTotals("gpt-5.5", 300, 100, 20), CodexRollout.Totals(RolloutText.Stream(text), "gpt-5.5"));

        // No model known: the whole file is read for it.
        Assert.Equal(new RolloutTotals("gpt-5.4", 300, 100, 20), CodexRollout.Totals(RolloutText.Stream(text)));
    }

    [Fact]
    public void ATailWithoutATaskMarkerHasNoState()
    {
        Assert.Null(CodexRollout.ParseTail(RolloutText.Stream(RolloutText.Join(RolloutText.Meta, RolloutText.Count("null", 1, 0, 1)))).State);
    }

    [Fact]
    public void AFileWithoutUsageHasZeroTotalsAndNoModel()
    {
        Assert.Equal(new RolloutTotals(null, 0, 0, 0), CodexRollout.Totals(RolloutText.Stream(RolloutText.Join(RolloutText.Meta, RolloutText.Started))));
    }

    [Theory]
    [InlineData("gpt-5.6-luna", true)]
    [InlineData("GPT-5.6-LUNA", true)]
    [InlineData("gpt-5.5", true)]
    [InlineData("gpt-5.2-codex", false)]
    [InlineData("gpt-5.1-codex-mini", false)]
    [InlineData("codex-auto-review", false)]
    [InlineData("gpt-5.5-2099-01-01", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyListedModelsHaveAPrice(string? model, bool priced) =>
        Assert.Equal(priced, OpenAiPrices.TryGet(model, out _));

    [Fact]
    public void CentsPriceUncachedCachedAndOutputSeparately()
    {
        Assert.True(OpenAiPrices.TryGet("gpt-5.5", out var price));

        Assert.Equal(500, OpenAiPrices.Cents(price, new RolloutTotals("gpt-5.5", 1_000_000, 0, 0)));
        Assert.Equal(50, OpenAiPrices.Cents(price, new RolloutTotals("gpt-5.5", 1_000_000, 1_000_000, 0)));
        Assert.Equal(3000, OpenAiPrices.Cents(price, new RolloutTotals("gpt-5.5", 0, 0, 1_000_000)));
        Assert.Equal(0, OpenAiPrices.Cents(price, default));
    }

    // Counts only Read(byte[]): a span read on a MemoryStream subclass is routed through it, so counting both would count twice.
    private sealed class Counting(byte[] data) : MemoryStream(data)
    {
        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = base.Read(buffer, offset, count);
            BytesRead += n;
            return n;
        }
    }
}
