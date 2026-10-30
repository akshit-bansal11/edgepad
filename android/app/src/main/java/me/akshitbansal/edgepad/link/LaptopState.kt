package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.DeviceOs
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.PadStatus
import me.akshitbansal.edgepad.protocol.SignStatus
import me.akshitbansal.edgepad.protocol.TextKind
import java.util.Base64

/** One character of TEXT MACRO_OPEN: what the laptop knows of a macro's app. An unknown one reads as [UNTRACKED]. */
enum class OpenState(
    val char: Char,
) {
    /** Nothing the laptop can follow: a link, a folder, a document. */
    UNTRACKED('-'),

    CLOSED('0'),
    OPEN('1'),

    /** Open and in front of everything else. */
    FRONT('2'),
    ;

    val isOpen: Boolean get() = this == OPEN || this == FRONT

    companion object {
        fun of(char: Char): OpenState = entries.firstOrNull { it.char == char } ?: UNTRACKED
    }
}

/**
 * The latest of everything the laptop has reported: levels, mute and play flags, what is playing and
 * where in it. The activity keeps one per laptop, so a control surface built later (reopened, rotated,
 * themed) starts from what the laptop last said instead of from zero. Touched on the UI thread only.
 */
class LaptopState {
    private val levels = HashMap<ControlId, Int>()
    private val flags = HashMap<ControlId, Boolean>()

    /**
     * The laptop's identity from TEXT DEVICE. Null until the laptop says so, which is also where a laptop too old
     * to send it stays. [os] is null for a token this build does not know, so an OS-dependent hint is not shown.
     */
    var laptopId: String? = null
        private set

    var laptopName: String? = null
        private set

    var os: DeviceOs? = null
        private set

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
     * What the laptop last said of each macro slot's app, in [macros] order. Empty until it says, which is also what
     * a laptop that cannot tell leaves it as, so no macro reads as open.
     */
    var macroOpen: List<OpenState> = emptyList()
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

    /**
     * What this laptop says it can do beyond the basics. Empty until the laptop says, which is also what a
     * laptop that predates the list leaves it as, so every optional screen starts hidden.
     */
    var features: Set<Feature> = emptySet()
        private set

    /**
     * Forgets [features] for a new connection. They are said once a connection and never withdrawn, so the same
     * laptop updated to a build without one, or older than TEXT 9, would otherwise keep offering what it last had.
     */
    fun forgetFeatures() {
        features = emptySet()
    }

    /** Forgets [macroOpen] when a link ends or a new one starts: the laptop says it afresh when next watched. */
    fun forgetMacroOpen() {
        macroOpen = emptyList()
    }

    /**
     * The laptop's primary display in pixels as (width, height), or null until it says. The annotate surface
     * is shaped to this ratio. Kept here, not on the screen, because the laptop sends it once and a rotation
     * rebuilds the screen.
     */
    var screenSize: Pair<Int, Int>? = null
        private set

    /** How the last signature save ended, or null while none has been asked for or answered. */
    var signStatus: SignStatus? = null
        private set

    /**
     * The slot whose icon a [take] that returned true just finished, or -1. Read straight after that call, so
     * an open grid can redraw that one button rather than decoding every picture it holds over again.
     */
    var lastIcon = -1
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

    /** Whether the laptop last said the app of macro slot [slot] is open, in front or not. */
    fun isOpen(slot: Int): Boolean = macroOpen.getOrNull(slot)?.isOpen == true

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
                    TextKind.MACRO_OPEN -> macroOpen = frame.text.take(MACRO_SLOTS).map(OpenState::of)
                    TextKind.MACRO_ICON -> return iconChunk(frame.text)
                    TextKind.PAD_STATUS -> padStatus = PadStatus.of(frame.text)
                    TextKind.FEATURES -> features = features(frame.text)
                    TextKind.SCREEN_SIZE -> screenSize = size(frame.text) ?: return false
                    TextKind.SIGN_STATUS -> signStatus = SignStatus.of(frame.text)
                    TextKind.DEVICE -> return device(frame.text)
                    else -> return false
                }
            }

            else -> {
                return false
            }
        }
        return true
    }

    /**
     * "idhex/os/name". Malformed text (fewer than two slashes, an id that is not 32 hex digits, an empty name) is
     * ignored and the last good identity stays. The name may contain slashes, so only the first two split.
     */
    private fun device(text: String): Boolean {
        val parts = text.split('/', limit = DEVICE_FIELDS)
        if (parts.size != DEVICE_FIELDS || !ID_HEX.matches(parts[0]) || parts[2].isEmpty()) return false
        laptopId = parts[0]
        os = DeviceOs.of(parts[1])
        laptopName = parts[2]
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

    /** A token this build does not know is skipped, not an error: a newer laptop may name more. */
    private fun features(text: String): Set<Feature> = text.split('/').mapNotNull(Feature::of).toSet()

    /** "1920/1080". Null for anything else, so a malformed size leaves the last good one in place. */
    private fun size(text: String): Pair<Int, Int>? {
        val parts = text.split('/')
        val width = parts.getOrNull(0)?.toIntOrNull() ?: return null
        val height = parts.getOrNull(1)?.toIntOrNull() ?: return null
        return if (parts.size == 2 && width > 0 && height > 0) width to height else null
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
        lastIcon = slot
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
        // A blank name keeps its slot rather than being dropped: the phone sends an index, so removing the
        // empty name from "Chrome//Notes" would put Notes where the laptop expects the missing slot and
        // launch the wrong thing. Anything past the reserved block has no action id to reach it.
        val names = text.split('/').take(MACRO_SLOTS).map(String::trim)
        val list = if (names.none { it.isNotEmpty() }) emptyList() else names
        // The cache is keyed by slot and name together: a slot whose macro is deleted, renamed or replaced by
        // another loses its icon, since one kept against the number would end up on whatever moved into it,
        // and one that is unchanged keeps it, so the grid opens with its pictures already there. A macro whose
        // icon alone was edited is sent again after this list (see wantsIcons) and replaces the old one.
        icons.keys.retainAll { slot ->
            val name = list.getOrNull(slot)
            !name.isNullOrEmpty() && name == macros.getOrNull(slot)
        }
        parts.clear()
        // A slot number now means another macro, so what was said of the old one must not stay on it.
        if (list != macros) forgetMacroOpen()
        return list
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

        /** "idhex/os/name". */
        private const val DEVICE_FIELDS = 3

        /** A link id: 16 bytes, written as 32 hex digits. */
        private val ID_HEX = Regex("[0-9a-fA-F]{32}")
        private const val MAX_LEVEL = 100

        /** "slot/chunk/chunks/base64". */
        private const val ICON_FIELDS = 4

        /**
         * The most chunks one icon may claim. The laptop caps an icon at ninety-six kilobytes at 512px, which
         * is 547 of them, so this is headroom rather than a limit anything real meets — and a ceiling on what a
         * malformed count can ask this app to allocate.
         *
         * It was 64 while the laptop sent 48px squares capped at six kilobytes. The square grew to 128 in
         * 3.0.1, and this had to grow with it in the same change: a count over the ceiling is refused here,
         * and a refused chunk is an icon that never arrives rather than one that arrives smaller.
         */
        private const val MAX_CHUNKS = 600
    }
}
