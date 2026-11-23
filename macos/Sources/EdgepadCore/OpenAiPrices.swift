import Foundation

/// USD per million tokens of one model.
public struct OpenAiPrice: Equatable {
    public let input: Decimal
    public let cachedInput: Decimal
    public let output: Decimal

    init(_ input: String, _ cachedInput: String, _ output: String) {
        self.input = Decimal(string: input, locale: Locale(identifier: "en_US_POSIX")) ?? 0
        self.cachedInput = Decimal(string: cachedInput, locale: Locale(identifier: "en_US_POSIX")) ?? 0
        self.output = Decimal(string: output, locale: Locale(identifier: "en_US_POSIX")) ?? 0
    }
}

/// What a Codex session would cost at OpenAI's API list prices, which is an estimate: a subscription is not billed per
/// token. Source: https://developers.openai.com/api/docs/pricing, Standard tier, short context, fetched 2026-11-19, the
/// same table as the laptop core's OpenAiPrices.cs. A model that is not here is unpriced (counted as zero cents and
/// reported as such), never guessed. Above 272K input tokens gpt-5.5 and gpt-5.4 cost more; the short-context price is
/// used for them, so a long-context session is under-estimated. Cache writes are counted as plain input.
public enum OpenAiPrices {
    private static let table: [String: OpenAiPrice] = [
        "gpt-5.6-sol": OpenAiPrice("4.00", "0.40", "20.00"),
        "gpt-5.6-terra": OpenAiPrice("2.00", "0.20", "12.00"),
        "gpt-5.6-luna": OpenAiPrice("0.20", "0.02", "1.20"),
        "gpt-5.6-cyber": OpenAiPrice("12.50", "1.25", "75.00"),
        "gpt-5.5": OpenAiPrice("5.00", "0.50", "30.00"),
        "gpt-5.4": OpenAiPrice("2.50", "0.25", "15.00"),
        "gpt-5.4-mini": OpenAiPrice("0.75", "0.075", "4.50"),
        "gpt-5.4-nano": OpenAiPrice("0.20", "0.02", "1.25"),
        "gpt-5.3-codex": OpenAiPrice("1.75", "0.175", "14.00"),
        "gpt-5.2": OpenAiPrice("1.75", "0.175", "14.00"),
        "gpt-5.1": OpenAiPrice("1.25", "0.125", "10.00"),
        "gpt-5": OpenAiPrice("1.25", "0.125", "10.00"),
        "gpt-5-mini": OpenAiPrice("0.25", "0.025", "2.00"),
        "gpt-5-nano": OpenAiPrice("0.05", "0.005", "0.40"),
    ]

    /// The price of `model`, case-insensitively, or nil when it is not listed.
    public static func price(_ model: String?) -> OpenAiPrice? {
        guard let model else { return nil }
        return table[model.lowercased()]
    }

    /// Whole cents, rounded to the nearest, for the uncached input, the cached input and the output of `totals`.
    public static func cents(_ price: OpenAiPrice, _ totals: RolloutTotals) -> Int64 {
        var dollarsTimesMillion =
            Decimal(totals.uncached) * price.input + Decimal(totals.cachedInput) * price.cachedInput
            + Decimal(totals.output) * price.output
        dollarsTimesMillion /= 10_000
        var rounded = Decimal()
        NSDecimalRound(&rounded, &dollarsTimesMillion, 0, .plain)
        return NSDecimalNumber(decimal: rounded).int64Value
    }
}
