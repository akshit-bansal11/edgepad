package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.PowerStateToken

/**
 * The laptop's armed restart or shut down, from TEXT POWER_STATE ("restart/60"). The laptop says it once, when it
 * arms, so the phone counts down on its own clock from [receivedAtMs]. Mirrors protocol/actions.txt.
 */
data class PowerState(
    val kind: PowerStateToken,
    val seconds: Int,
    val receivedAtMs: Long,
) {
    /** Whole seconds still to go at [nowMs], rounded up so it shows the full count first, and never below 0. */
    fun secondsLeft(nowMs: Long): Int {
        val leftMs = seconds * MS - (nowMs - receivedAtMs)
        return ((leftMs + MS - 1) / MS).coerceIn(0L, seconds.toLong()).toInt()
    }

    companion object {
        private const val MS = 1000L

        /** A clock that only moves forward, in milliseconds. */
        fun now(): Long = System.nanoTime() / 1_000_000L

        /** `kind/seconds`. Null for empty text, a kind this build does not know, or seconds that are not a number. */
        fun parse(
            text: String,
            nowMs: Long,
        ): PowerState? {
            val slash = text.indexOf('/')
            if (slash < 0) return null
            val kind = PowerStateToken.of(text.substring(0, slash)) ?: return null
            val seconds = text.substring(slash + 1).toIntOrNull()?.takeIf { it >= 0 } ?: return null
            return PowerState(kind, seconds, nowMs)
        }
    }
}
