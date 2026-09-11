package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame

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
                when (frame.kind) {
                    NOW_PLAYING -> nowPlaying = frame.text
                    APP -> app = frame.text
                    TIMELINE -> timeline(frame.text)
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

    companion object {
        /** TEXT kinds, as the laptop numbers them. */
        const val NOW_PLAYING = 0
        const val APP = 1
        const val TIMELINE = 2

        /** Phone to laptop: characters to type; "\b" is backspace and "\n" is enter. */
        const val TYPE = 3

        private const val FLAG_BIT = 1
        private const val MAX_LEVEL = 100
    }
}
