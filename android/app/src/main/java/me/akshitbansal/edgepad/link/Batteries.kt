package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.BatteryKind
import me.akshitbansal.edgepad.protocol.OutputKind

/** One device from TEXT BATTERIES. [level] is 0..100. */
data class BatteryEntry(
    val kind: BatteryKind,
    val level: Int,
    val charging: Boolean,
    val name: String,
)

/** The decoder of TEXT BATTERIES: `kind:level:charging:name` entries joined by `|`. */
object Batteries {
    private const val FIELDS = 4
    private const val MAX_LEVEL = 100

    /** Malformed entries (wrong field count, a level that is not a number) are skipped; the rest still count. */
    fun parse(text: String): List<BatteryEntry> =
        text.split('|').mapNotNull { entry ->
            val parts = entry.split(':', limit = FIELDS)
            val level = parts.getOrNull(1)?.toIntOrNull()
            if (parts.size != FIELDS || level == null) {
                null
            } else {
                BatteryEntry(BatteryKind.of(parts[0]), level.coerceIn(0, MAX_LEVEL), parts[2] == "1", parts[3])
            }
        }
}

/** The default output device from TEXT OUTPUT. */
data class Output(
    val kind: OutputKind,
    val name: String,
) {
    companion object {
        /** `kind/name`, split on the first slash only. Null on empty text, no slash or an empty name. */
        fun parse(text: String): Output? {
            val slash = text.indexOf('/')
            if (slash < 0 || slash == text.length - 1) return null
            return Output(OutputKind.of(text.substring(0, slash)), text.substring(slash + 1))
        }
    }
}

/** A device name shortened for a small label. */
object OutputName {
    private const val MAX = 10

    /** Whole words while they fit in [MAX]; a first word longer than that is cut to 9 characters and an ellipsis. */
    fun short(name: String): String {
        if (name.length <= MAX) return name
        var shown = ""
        for (word in name.split(' ').filter { it.isNotEmpty() }) {
            val next = if (shown.isEmpty()) word else "$shown $word"
            if (next.length > MAX) break
            shown = next
        }
        if (shown.isNotEmpty()) return shown
        var end = MAX - 1
        if (Character.isLowSurrogate(name[end]) && Character.isHighSurrogate(name[end - 1])) end--
        return name.substring(0, end) + "…"
    }
}
