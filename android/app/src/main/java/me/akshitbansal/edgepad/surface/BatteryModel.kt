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

data class BatteryGroup(
    val name: String,
    val rows: List<BatteryRow>,
)

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

fun interface BatterySource {
    fun groups(): BatteryGroups

    object None : BatterySource {
        override fun groups() = BatteryGroups(null, emptyList(), null)
    }
}

object BatteryModel {
    const val LOW = 20

    fun low(
        level: Int,
        charging: Boolean,
    ) = level < LOW && !charging

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

    fun chips(g: BatteryGroups): BatteryChips {
        val laptop = g.inUse?.rows?.firstOrNull { it.kind == BatteryKind.LAPTOP }
        val hidden =
            (g.inUse
                ?.rows
                .orEmpty()
                .filter { it !== laptop } + g.others.flatMap { it.rows })
        return BatteryChips(laptop, g.phone, hidden.size, hidden.any { it.low })
    }

    private fun group(v: LaptopView) =
        BatteryGroup(v.name, v.entries.map { BatteryRow(it.name, it.kind, false, it.level, it.charging, v.name) })
}
