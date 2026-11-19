// Adapted from Plimsoll by Akshit Bansal, MIT (scripts/prices.mjs). Added: isPriced.
//
// Anthropic API list prices in US dollars per million tokens, as of 2026-10-20.
// Used to ESTIMATE cost from transcript tokens: a subscription is not billed
// this way. Update the table when prices change.

// [model id prefix, input $/MTok, output $/MTok, cache-read multiplier of input]
// Order matters: a longer prefix must be listed before a shorter one it starts
// with (claude-opus-5-5 before claude-opus-5, claude-opus-4-1 before claude-opus-4),
// the first match wins.
const PRICES = [
    ['claude-fable-5-1', 10, 50, 0.025],
    ['claude-mythos-5-1', 10, 50, 0.025],
    ['claude-fable-5', 10, 50, 0.1],
    ['claude-mythos-5', 10, 50, 0.1],
    ['claude-opus-5-5', 4, 20, 0.05],
    ['claude-opus-5', 5, 25, 0.1],
    ['claude-opus-4-8', 5, 25, 0.1],
    ['claude-opus-4-7', 5, 25, 0.1],
    ['claude-opus-4-6', 5, 25, 0.1],
    ['claude-opus-4-5', 5, 25, 0.1],
    ['claude-opus-4-1', 15, 75, 0.1],
    ['claude-opus-4', 15, 75, 0.1],
    ['claude-sonnet-5-5', 2, 10, 0.05],
    ['claude-sonnet-5', 2, 10, 0.1],
    ['claude-sonnet-4-6', 3, 15, 0.1],
    ['claude-sonnet-4-5', 3, 15, 0.1],
    ['claude-sonnet-4', 3, 15, 0.1],
    ['claude-haiku-5-5', 0.1, 0.5, 0.1],
    ['claude-haiku-4-5', 1, 5, 0.1],
    ['claude-3-5-haiku', 0.8, 4, 0.1],
]
const WRITE_5M = 1.25
const WRITE_1H = 2
// Claude Haiku 5.5 charges 5x every price for a request whose prompt (input plus cache reads
// and writes) is over 100,000 tokens.
const LONG_PROMPT = { prefix: 'claude-haiku-5-5', over: 100_000, times: 5 }

// Bedrock (us.anthropic.claude-opus-4-1-...) and Vertex (claude-sonnet-4-5@...) ids carry a prefix
const keyOf = model => {
    const id = String(model)
    const at = id.indexOf('claude-')
    return at > 0 ? id.slice(at) : id
}
const rowOf = key => PRICES.find(([prefix]) => key.startsWith(prefix))

// False for a model the table does not know: its tokens count, its dollars do not.
export const isPriced = model => rowOf(keyOf(model)) !== undefined

export const costOf = (model, usage) => {
    const key = keyOf(model)
    const row = rowOf(key)
    if (!row) return 0 // an unknown model adds nothing rather than a guessed price
    const [, input, output, readMultiplier] = row
    // usage.cache_creation splits the write by lifetime when present; without it every written token is 5-minute
    const write1h = usage.cache_creation?.ephemeral_1h_input_tokens ?? 0
    const write5m = Math.max(0, (usage.cache_creation_input_tokens ?? 0) - write1h)
    const usd =
        ((usage.input_tokens ?? 0) * input +
            (usage.output_tokens ?? 0) * output +
            (usage.cache_read_input_tokens ?? 0) * input * readMultiplier +
            write5m * input * WRITE_5M +
            write1h * input * WRITE_1H) /
        1e6
    const prompt =
        (usage.input_tokens ?? 0) + (usage.cache_read_input_tokens ?? 0) + (usage.cache_creation_input_tokens ?? 0)
    return key.startsWith(LONG_PROMPT.prefix) && prompt > LONG_PROMPT.over ? usd * LONG_PROMPT.times : usd
}
