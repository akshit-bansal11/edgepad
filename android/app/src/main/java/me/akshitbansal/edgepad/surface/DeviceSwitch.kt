package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.link.LaptopConnections

/**
 * What the Devices dial needs of the connected laptops, and nothing it could send a frame with: stepping moves the
 * phone's own selection, so the dial never emits one. Only laptops that are connected count, because a dial that
 * stopped on a lost one would leave the pad on a reconnect screen.
 */
interface DeviceSwitch {
    /** The names of the connected laptops, in the order Devices lists them. */
    val names: List<String>

    /** Where the selected laptop is in [names], or -1 when it is not among them. */
    val activeIndex: Int

    /** Selects the next laptop for [direction] above zero, the previous for below; stops at either end. */
    fun step(direction: Int)

    /** Opens the Devices list. */
    fun open()

    /** No laptops, for a surface that has no Devices dial to serve. */
    object None : DeviceSwitch {
        override val names: List<String> = emptyList()
        override val activeIndex = -1

        override fun step(direction: Int) = Unit

        override fun open() = Unit
    }

    companion object {
        /** Longest name the ruler shows before the ellipsis; the ruler is a corner's width, not a row. */
        const val MAX_NAME = 10

        /**
         * The index [direction] moves to from [active] among [count] laptops, or null when it stays put. It clamps
         * rather than wraps: one slide that runs past the end would otherwise lap the list and land back where it
         * started. With no active laptop a step lands on the first, or the last going back.
         */
        fun neighbour(
            count: Int,
            active: Int,
            direction: Int,
        ): Int? {
            if (count <= 0 || direction == 0) return null
            val target =
                when {
                    active < 0 -> if (direction > 0) 0 else count - 1
                    else -> (active + if (direction > 0) 1 else -1).coerceIn(0, count - 1)
                }
            return target.takeIf { it != active }
        }

        /** [name] cut to [MAX_NAME] characters with an ellipsis at the end when it is longer. */
        fun fit(name: String): String = if (name.length <= MAX_NAME) name else name.take(MAX_NAME - 1).trimEnd() + "…"
    }
}

/** [DeviceSwitch] over the manager: the connected laptops, and a selection that moves through [LaptopConnections]. */
class ConnectedLaptops(
    private val manager: LaptopConnections,
    private val onOpen: () -> Unit,
) : DeviceSwitch {
    private val connected get() = manager.laptops.filter { it.connected }

    override val names: List<String> get() = connected.map { it.name }

    override val activeIndex: Int get() = connected.indexOfFirst { it === manager.selected }

    override fun step(direction: Int) {
        val live = connected
        val target = DeviceSwitch.neighbour(live.size, live.indexOfFirst { it === manager.selected }, direction)
        target?.let { manager.select(live[it].id) }
    }

    override fun open() = onOpen()
}
