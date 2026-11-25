package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.link.BatteryEntry
import me.akshitbansal.edgepad.protocol.BatteryKind

/** One device line. [kind] is null for the phone. */
data class BatteryRow(
    val name: String,
    val kind: BatteryKind?,
    val isPhone: Boolean,
    val level: Int,
    val charging: Boolean,
    val group: String,
) {
    /** Under [BatteryModel.LOW] and not charging. */
    val low: Boolean get() = BatteryModel.low(level, charging)
}

/** One laptop as the model sees it. [id] is the laptop's own id, so one seen over two transports counts once. */
data class LaptopView(
    val id: String,
    val name: String,
    val entries: List<BatteryEntry>,
)

/** One heading of the battery list, a laptop's or the phone's, and the device lines under it. */
data class BatteryGroup(
    val name: String,
    val rows: List<BatteryRow>,
)

/** The battery list in the order it is shown: the laptop in use, the other connected laptops, then this phone. */
data class BatteryGroups(
    val inUse: BatteryGroup?,
    val others: List<BatteryGroup>,
    val phone: BatteryRow?,
)

/** What the strip draws: at most laptop, phone, +[plus]. [plus] 0 means no chip. */
data class BatteryChips(
    val laptop: BatteryRow?,
    val phone: BatteryRow?,
    val plus: Int,
    val plusLow: Boolean,
) {
    val isEmpty: Boolean get() = laptop == null && phone == null && plus == 0
}

/** Where the strip and the battery sheet read the groups from, as they are now. */
fun interface BatterySource {
    fun groups(): BatteryGroups

    object None : BatterySource {
        override fun groups() = BatteryGroups(null, emptyList(), null)
    }
}

/**
 * Pure rules behind the battery strip and sheet: which level counts as low, how laptops are grouped, and what the strip
 * has room for.
 */
object BatteryModel {
    const val LOW = 20

    /** Whether [level] is under [LOW] percent and the device is not charging, which is when it is drawn in the danger colour. */
    fun low(
        level: Int,
        charging: Boolean,
    ) = level < LOW && !charging

    /**
     * Groups for the [selected] laptop, then each of [others] not already shown (one seen over two transports counts
     * once), then [phone].
     */
    fun build(
        selected: LaptopView?,
        others: List<LaptopView>,
        phone: BatteryRow?,
    ): BatteryGroups {
        val seen = HashSet<String>()
        selected?.let { seen.add(it.id) }
        val rest = others.filter { seen.add(it.id) }
        return BatteryGroups(selected?.let(::group), rest.map(::group), phone)
    }

    /**
     * What the strip draws for [g]: the laptop's own battery, the phone's, and a count of every other line, with whether
     * any of those is low.
     */
    fun chips(g: BatteryGroups): BatteryChips {
        val laptop = g.inUse?.rows?.firstOrNull { it.kind == BatteryKind.LAPTOP }
        val rows = g.inUse?.rows ?: emptyList()
        val own = rows.filter { it !== laptop }
        val hidden = own + g.others.flatMap { it.rows }
        return BatteryChips(laptop, g.phone, hidden.size, hidden.any { it.low })
    }

    private fun group(v: LaptopView) =
        BatteryGroup(v.name, v.entries.map { BatteryRow(it.name, it.kind, false, it.level, it.charging, v.name) })
}

/** The strip's chip geometry in dp, kept apart from the drawing so it can be tested. */
object BatteryShape {
    const val ICON_DP = 13f
    const val BODY_W_DP = 22f
    const val BODY_H_DP = 11f
    const val STROKE_DP = 1.5f
    const val CORNER_DP = 3f
    const val INNER_PAD_DP = 1.5f
    const val FILL_CORNER_DP = 1.5f
    const val NUB_W_DP = 2f
    const val NUB_H_DP = 5.5f
    const val NUB_GAP_DP = 1f
    const val BOLT_DP = 11f

    /** Between the parts of one chip. */
    const val GAP_DP = 5f

    /** Between two chips. */
    const val SPACING_DP = 24f
    private const val FULL = 100
    private const val INSET = 2 * (STROKE_DP + INNER_PAD_DP)

    /** The fill's width at [level] percent: the body less its border and padding, times the level. */
    fun fillWidth(level: Int): Float = (BODY_W_DP - INSET) * level.coerceIn(0, FULL) / FULL

    fun fillHeight(): Float = BODY_H_DP - INSET

    /** What a chip takes besides its text: the +N chip is text alone. */
    fun fixedWidth(
        device: Boolean,
        icon: Boolean,
        charging: Boolean,
    ): Float {
        if (!device) return 0f
        var width = BODY_W_DP + NUB_GAP_DP + NUB_W_DP + GAP_DP
        if (icon) width += ICON_DP + GAP_DP
        if (charging) width += GAP_DP + BOLT_DP
        return width
    }

    /** [count] chips side by side, [SPACING_DP] apart. */
    fun span(
        widths: FloatArray,
        count: Int,
    ): Float {
        var span = (count - 1).coerceAtLeast(0) * SPACING_DP
        for (i in 0 until count) span += widths[i]
        return span
    }

    /** The scale of the text, from 1 up to any size that is needed, at which [fixed] and the text fit [available]. */
    fun neededScale(
        fixed: Float,
        textAtBase: Float,
        available: Float,
    ): Float = if (textAtBase <= 0f) 1f else ((available - fixed) / textAtBase).coerceIn(0f, 1f)

    /** Low is red whatever else is true, charging is the accent, and the rest is plain ink. */
    fun fillColor(
        low: Boolean,
        charging: Boolean,
        danger: Int,
        accent: Int,
        ink: Int,
    ): Int =
        when {
            low -> danger
            charging -> accent
            else -> ink
        }
}
