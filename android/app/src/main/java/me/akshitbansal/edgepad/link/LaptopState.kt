package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.TextKind

/**
 * The latest of everything the laptop has reported: levels, mute and play flags, what is playing and
 * where in it. The activity keeps one per laptop, so a control surface built later (reopened, rotated,
 * themed) starts from what the laptop last said instead of from zero. Touched on the UI thread only.
 */
class LaptopState {
    private val levels = HashMap<ControlId, Int>()
    private val flags = HashMap<ControlId, Boolean>()

    var nowPlaying = ""
        private set

    /** The name of the app playing it, as the laptop's taskbar would show it. */
    var app = ""
        private set

    /** Seconds into the track, or -1 when the player reports no timeline. */
    var position = -1
        private set

    /** The track's length in seconds, or 0 when unknown. */
    var duration = 0
        private set

    /**
     * The laptop display's available refresh rates in hertz, in the order a REFRESH_RATE value indexes them.
     * Empty until the laptop names them, which is also what a laptop that cannot change its rate reports.
     */
    var refreshRates: List<Int> = emptyList()
        private set

    /**
     * The names of the laptop's macro slots, in the order MACRO_BASE indexes them. Empty until the laptop
     * names them, which is also what a laptop with no macros configured reports.
     */
    var macros: List<String> = emptyList()
        private set

    /** The last level the laptop reported for [control], or null when it has not reported one. */
    fun level(control: ControlId): Int? = levels[control]

    /** Muted for audio controls, playing for media. */
    fun flag(control: ControlId): Boolean = flags[control] == true

    /** Takes in a frame from the laptop. True when it was state this keeps, so the surface should redraw. */
    fun take(frame: Frame): Boolean {
        when (frame) {
            is Frame.StateReport -> {
                val control = ControlId.of(frame.control) ?: return false
                levels[control] = frame.value.coerceIn(0, MAX_LEVEL)
                flags[control] = (frame.flags and FLAG_BIT) != 0
            }

            is Frame.Text -> {
                when (TextKind.of(frame.kind)) {
                    TextKind.NOW_PLAYING -> nowPlaying = frame.text
                    TextKind.APP -> app = frame.text
                    TextKind.TIMELINE -> timeline(frame.text)
                    TextKind.REFRESH_RATES -> refreshRates = rates(frame.text)
                    TextKind.MACROS -> macros = macroNames(frame.text)
                    else -> return false
                }
            }

            else -> {
                return false
            }
        }
        return true
    }

    /** "84/227": seconds in, then the length. Anything else means there is no timeline. */
    private fun timeline(text: String) {
        val parts = text.split('/')
        val seconds = parts.getOrNull(0)?.toIntOrNull()
        val length = parts.getOrNull(1)?.toIntOrNull()
        if (parts.size == 2 && seconds != null && length != null && length > 0) {
            position = seconds.coerceIn(0, length)
            duration = length
        } else {
            position = -1
            duration = 0
        }
    }

    /** "60/120/144": the rates a REFRESH_RATE index counts through, in the laptop's own order. */
    private fun rates(text: String): List<Int> {
        val parts = text.split('/').map(String::toIntOrNull)
        // Dropping one unreadable entry would shift every index after it onto the wrong rate, so a list
        // with anything unreadable in it is no list at all and the dial stays where it was.
        return if (parts.any { it == null }) emptyList() else parts.filterNotNull()
    }

    /** "Chrome/Spotify/Notes": one name per macro slot, in the order MACRO_BASE indexes them. */
    private fun macroNames(text: String): List<String> {
        // A blank name keeps its slot rather than being dropped: the phone sends an index, so removing the
        // empty name from "Chrome//Notes" would put Notes where the laptop expects the missing slot and
        // launch the wrong thing. Anything past the reserved block has no action id to reach it.
        val names = text.split('/').take(MACRO_SLOTS).map(String::trim)
        return if (names.none { it.isNotEmpty() }) emptyList() else names
    }

    private companion object {
        const val FLAG_BIT = 1
        const val MAX_LEVEL = 100

        /**
         * The grid the phone draws, 5 by 3. The reserved action block 64..95 is wider on purpose, but the
         * laptop never fills past this: fifteen names of sixteen bytes are all one TEXT frame can carry.
         */
        const val MACRO_SLOTS = 15
    }
}
