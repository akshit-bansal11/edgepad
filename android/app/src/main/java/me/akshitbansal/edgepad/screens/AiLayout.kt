package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.protocol.AgentToken
import me.akshitbansal.edgepad.protocol.Feature

/** One agent the AI page can show: the feature that says it is connected, its logo, and whether it reports limits. */
data class AiTool(
    val agent: AgentToken,
    val feature: Feature,
    val logoId: String,
    val hasLimits: Boolean,
    val brand: Int,
)

/** The AI page's layout maths, pure so a table can test it. All sizes are dp. */
object AiLayout {
    /** The agents the phone knows, in the order their columns take. */
    val tools =
        listOf(
            AiTool(AgentToken.CLAUDE, Feature.CLAUDE, "claude", true, 0xFFD97757.toInt()),
            AiTool(AgentToken.CODEX, Feature.CODEX, "codex", true, 0xFF2B2B2B.toInt()),
            AiTool(AgentToken.GEMINI, Feature.GEMINI, "gemini", false, 0xFF4285F4.toInt()),
            AiTool(AgentToken.OPENCODE, Feature.OPENCODE, "opencode", false, 0xFF3A3A3A.toInt()),
        )

    const val MAX_RINGS = 3
    const val PAGE_DP = 16f
    const val GAP_DP = 10f
    const val CARD_PAD_DP = 14f
    const val MIN_RING_DP = 44f
    const val MAX_RING_DP = 88f

    /** The smallest ring that still has room for a reset line under its caption. */
    const val SUB_RING_DP = 64f

    /** One column per connected tool, one to four. */
    fun columns(tools: Int): Int = tools.coerceIn(1, MAX_COLUMNS)

    /** Sideways the columns sit side by side and share the width; upright each takes a row of its own. */
    fun columnWidthDp(
        screenWidthDp: Float,
        columns: Int,
        landscape: Boolean,
    ): Float {
        val inner = screenWidthDp - 2 * PAGE_DP
        return if (landscape) (inner - (columns - 1) * GAP_DP) / columns else inner
    }

    /** The ring's side: what a card holds across [rings] rings, kept between [MIN_RING_DP] and [MAX_RING_DP]. */
    fun ringDp(
        columnWidthDp: Float,
        rings: Int,
    ): Float = ((columnWidthDp - 2 * CARD_PAD_DP) / rings.coerceAtLeast(1)).coerceIn(MIN_RING_DP, MAX_RING_DP)

    /** Whether a ring this size shows its reset line. */
    fun showsReset(ringDp: Float): Boolean = ringDp >= SUB_RING_DP

    /** The logo tile's side: smaller once three or four columns share the width. */
    fun logoDp(columns: Int): Float = if (columns <= 2) 28f else 24f

    private const val MAX_COLUMNS = 4
}

/** Colour contrast for the brand tiles, pure so a table can test it. Colours are ARGB ints. */
object TileContrast {
    private const val NEAR_BLACK = 0xFF111111.toInt()
    private const val WHITE = 0xFFFFFFFF.toInt()
    private const val LOW = 1.6

    /** The letter colour that reads on [tile]: white on a dark tile, near-black on a light one. */
    fun letter(tile: Int): Int = if (ratio(tile, WHITE) >= ratio(tile, NEAR_BLACK)) WHITE else NEAR_BLACK

    /** Whether [tile] is close enough to [ground] that it needs an outline to show. */
    fun needsOutline(
        tile: Int,
        ground: Int,
    ): Boolean = ratio(tile, ground) < LOW

    private fun ratio(
        a: Int,
        b: Int,
    ): Double {
        val la = luminance(a)
        val lb = luminance(b)
        return (maxOf(la, lb) + 0.05) / (minOf(la, lb) + 0.05)
    }

    private fun luminance(argb: Int): Double {
        fun channel(shift: Int): Double {
            val c = ((argb shr shift) and 0xFF) / 255.0
            return if (c <= 0.03928) c / 12.92 else Math.pow((c + 0.055) / 1.055, 2.4)
        }
        return 0.2126 * channel(16) + 0.7152 * channel(8) + 0.0722 * channel(0)
    }
}
