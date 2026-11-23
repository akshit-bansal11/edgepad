namespace Edgepad.Dashboard;

/// <summary>USD per million tokens of one model.</summary>
internal readonly record struct OpenAiPrice(decimal Input, decimal CachedInput, decimal Output);

/// <summary>
/// What a Codex session would cost at OpenAI's API list prices, which is an estimate: a subscription is not billed per token.
/// Source: https://developers.openai.com/api/docs/pricing, Standard tier, short context, fetched 2026-11-19. A model that is not
/// here is unpriced (counted as zero cents and reported as such), never guessed. Above 272K input tokens gpt-5.5 and gpt-5.4 cost
/// more; the short-context price is used for them, so a long-context session is under-estimated. Cache writes are counted as plain
/// input because the models Codex mostly runs have no cache-write price.
/// </summary>
internal static class OpenAiPrices
{
    private static readonly Dictionary<string, OpenAiPrice> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gpt-5.6-sol"] = new(4.00m, 0.40m, 20.00m),
        ["gpt-5.6-terra"] = new(2.00m, 0.20m, 12.00m),
        ["gpt-5.6-luna"] = new(0.20m, 0.02m, 1.20m),
        ["gpt-5.6-cyber"] = new(12.50m, 1.25m, 75.00m),
        ["gpt-5.5"] = new(5.00m, 0.50m, 30.00m),
        ["gpt-5.4"] = new(2.50m, 0.25m, 15.00m),
        ["gpt-5.4-mini"] = new(0.75m, 0.075m, 4.50m),
        ["gpt-5.4-nano"] = new(0.20m, 0.02m, 1.25m),
        ["gpt-5.3-codex"] = new(1.75m, 0.175m, 14.00m),
        ["gpt-5.2"] = new(1.75m, 0.175m, 14.00m),
        ["gpt-5.1"] = new(1.25m, 0.125m, 10.00m),
        ["gpt-5"] = new(1.25m, 0.125m, 10.00m),
        ["gpt-5-mini"] = new(0.25m, 0.025m, 2.00m),
        ["gpt-5-nano"] = new(0.05m, 0.005m, 0.40m),
    };

    public static bool TryGet(string? model, out OpenAiPrice price)
    {
        price = default;
        return model is not null && Table.TryGetValue(model, out price);
    }

    /// <summary>Whole cents, rounded to the nearest, for the uncached input, the cached input and the output of <paramref name="totals"/>.</summary>
    public static long Cents(OpenAiPrice price, RolloutTotals totals)
    {
        var dollarsTimesMillion = (totals.Uncached * price.Input) + (totals.CachedInput * price.CachedInput) + (totals.Output * price.Output);
        return (long)Math.Round(dollarsTimesMillion / 10_000m, MidpointRounding.AwayFromZero);
    }
}
