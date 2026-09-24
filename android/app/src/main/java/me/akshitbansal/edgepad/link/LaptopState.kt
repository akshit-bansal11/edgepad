package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.PadStatus
import me.akshitbansal.edgepad.protocol.TextKind
import java.util.Base64

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

    /**
     * The laptop's last word on whether it can offer a virtual controller, or null while it has said
     * nothing at all — which is also where a laptop too old to know PAD_ATTACH leaves it, since it drops
     * the action and never answers. The gamepad screen reads this to decide whether it is a controller or
     * a keyboard, so it is kept here rather than on the screen: the screen is rebuilt by a rotation and
     * the answer is not sent again.
     */
    var padStatus: PadStatus? = null
        private set

    /** Finished icons by slot. A slot with no entry has none, which is the ordinary case for most of them. */
    private val icons = HashMap<Int, ByteArray>()

    /** Icons still arriving, by slot: one slot of the array per chunk, filled in as the pieces land. */
    private val parts = HashMap<Int, Array<String?>>()

    /**
     * The PNG for macro slot [slot], or null until the laptop has sent a whole one. Bytes rather than a
     * Bitmap because decoding is Android's and this class is plain Kotlin the unit tests can reach.
     */
    fun macroIcon(slot: Int): ByteArray? = icons[slot]

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
                    TextKind.MACRO_ICON -> return iconChunk(frame.text)
                    TextKind.PAD_STATUS -> padStatus = PadStatus.of(frame.text)
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

    /**
     * One piece of one icon: "slot/chunk/chunks/base64". True only when that piece completed an icon, so the
     * grid is rebuilt once per icon rather than once per frame — thirty-odd redraws for one button would be
     * visible, and every one of them but the last would draw the same thing.
     *
     * Everything about the header is checked before it is used. The laptop is the trusted end of this link
     * and still gets checked, because the numbers here size an array and index into it: a count read out of
     * a corrupted frame would be an allocation this app cannot survive, and the guard costs one comparison.
     */
    private fun iconChunk(text: String): Boolean {
        val fields = text.split('/', limit = ICON_FIELDS)
        if (fields.size < ICON_FIELDS) return false
        val slot = fields[0].toIntOrNull() ?: return false
        val index = fields[1].toIntOrNull() ?: return false
        val count = fields[2].toIntOrNull() ?: return false
        if (slot !in 0 until MACRO_SLOTS || count !in 1..MAX_CHUNKS || index !in 0 until count) return false
        // A count that disagrees with what is already half-collected means a new icon for the slot — the
        // owner changed it and the laptop is sending again — so the old pieces go rather than being joined
        // to the new ones into something that is neither.
        val pieces =
            parts[slot]?.takeIf { it.size == count }
                ?: arrayOfNulls<String>(count).also { parts[slot] = it }
        pieces[index] = fields[3]
        if (pieces.any { it == null }) return false
        parts.remove(slot)
        val png = decode(pieces.joinToString("")) ?: return false
        icons[slot] = png
        return true
    }

    /** Null rather than a throw: a chunk that is not base64 is a frame to drop, not a link to close. */
    private fun decode(encoded: String): ByteArray? =
        try {
            Base64.getDecoder().decode(encoded)
        } catch (e: IllegalArgumentException) {
            null
        }

    /** "Chrome/Spotify/Notes": one name per macro slot, in the order MACRO_BASE indexes them. */
    private fun macroNames(text: String): List<String> {
        // A new list means the slots mean something else, so an icon kept against a slot number would end up
        // on whatever moved into it. The phone asks for them again when it sees this.
        icons.clear()
        parts.clear()
        // A blank name keeps its slot rather than being dropped: the phone sends an index, so removing the
        // empty name from "Chrome//Notes" would put Notes where the laptop expects the missing slot and
        // launch the wrong thing. Anything past the reserved block has no action id to reach it.
        val names = text.split('/').take(MACRO_SLOTS).map(String::trim)
        return if (names.none { it.isNotEmpty() }) emptyList() else names
    }

    companion object {
        /**
         * The grid the phone draws, 5 by 3. The reserved action block 64..95 is wider on purpose, but the
         * laptop never fills past this: fifteen names of sixteen bytes are all one TEXT frame can carry.
         *
         * Public, alone among the numbers here, because it is the ceiling on what a macro slot may be
         * anywhere on the phone: the shapes editor offers slots up to it and
         * [me.akshitbansal.edgepad.surface.Shapes] refuses a stored one past it. A second copy of the
         * number somewhere else would be a second thing to forget when the grid changes size.
         */
        const val MACRO_SLOTS = 15

        private const val FLAG_BIT = 1
        private const val MAX_LEVEL = 100

        /** "slot/chunk/chunks/base64". */
        private const val ICON_FIELDS = 4

        /**
         * The most chunks one icon may claim. The laptop caps an icon at twenty-four kilobytes, which is 137
         * of them, so this is headroom rather than a limit anything real meets — and a ceiling on what a
         * malformed count can ask this app to allocate.
         *
         * It was 64 while the laptop sent 48px squares capped at six kilobytes. The square grew to 128 in
         * 3.0.1, and this had to grow with it in the same change: a count over the ceiling is refused here,
         * and a refused chunk is an icon that never arrives rather than one that arrives smaller.
         */
        private const val MAX_CHUNKS = 160
    }
}
