package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.ActionId
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.sin

/** What a stroke is drawn as. [action] is what the phone sends to select it. */
enum class PenTool(
    val action: ActionId,
) {
    PEN(ActionId.TOOL_PEN),
    HIGHLIGHTER(ActionId.TOOL_HIGHLIGHTER),
    LINE(ActionId.TOOL_LINE),
    ARROW(ActionId.TOOL_ARROW),
    RECTANGLE(ActionId.TOOL_RECTANGLE),
    ELLIPSE(ActionId.TOOL_ELLIPSE),
    ;

    val isShape: Boolean get() = this == LINE || this == ARROW || this == RECTANGLE || this == ELLIPSE
}

/** A point as the laptop gets it: 0..65535 across its whole screen. */
data class WirePoint(
    val x: Int,
    val y: Int,
)

/** One finger-down to finger-up. A shape keeps the down point and the latest one, since that is all it is. */
class Stroke(
    val tool: PenTool,
    val colour: Int,
) {
    val points = ArrayList<WirePoint>()

    internal fun extend(point: WirePoint) {
        if (tool.isShape && points.size > 1) points[1] = point else points.add(point)
    }
}

/**
 * The phone's preview of what the laptop is drawing: the same tools, the same undo and clear, the same
 * shapes from the down point to the latest move. The laptop owns the real strokes and the real widths, so
 * this only has to look alike, not agree to the pixel. Pure, so it is tested on the JVM.
 */
class Strokes {
    private val done = ArrayList<Stroke>()

    var tool = PenTool.PEN
        private set

    var colour = 0
        private set

    /** Sign mode shows nothing on the laptop and takes only the pen, so the preview refuses the rest too. */
    var signing = false
        set(value) {
            field = value
            if (value) tool = PenTool.PEN
        }

    val strokes: List<Stroke> get() = done

    /** The stroke under the finger now; not yet undoable. */
    var active: Stroke? = null
        private set

    /** False when the tool is not allowed in sign mode. */
    fun select(tool: PenTool): Boolean {
        if (signing && tool != PenTool.PEN) return false
        this.tool = tool
        return true
    }

    /** False for an index outside the eight-colour palette, as the laptop drops it. */
    fun selectColour(index: Int): Boolean {
        if (index !in 0 until PALETTE_SIZE) return false
        colour = index
        return true
    }

    fun undo(): Boolean = done.removeLastOrNull() != null

    fun clear() {
        done.clear()
        active = null
    }

    /**
     * One PEN sample, [state] 0 up, 1 down, 2 move, the laptop's own rules: a move or up with no finger down
     * is ignored, and a down while one is open commits that one first, because its up was lost.
     */
    fun pen(
        point: WirePoint,
        state: Int,
    ): Boolean {
        when {
            state == STATE_DOWN -> {
                commit()
                active = Stroke(tool, colour).also { it.points.add(point) }
            }

            state == STATE_MOVE && active != null -> {
                if ((active?.points?.size ?: 0) >= MAX_POINTS) return false
                active?.extend(point)
            }

            state == STATE_UP && active != null -> {
                active?.extend(point)
                commit()
            }

            else -> {
                return false
            }
        }
        return true
    }

    private fun commit() {
        active?.let { done.add(it) }
        active = null
    }

    companion object {
        const val STATE_UP = 0
        const val STATE_DOWN = 1
        const val STATE_MOVE = 2
        const val PALETTE_SIZE = 8

        /** The laptop caps a stroke so a held finger cannot grow it forever; the preview matches. */
        const val MAX_POINTS = 20_000

        /** Pen and highlighter widths as a share of the screen's height, as on the laptop. */
        const val PEN_SHARE = 0.004f
        const val HIGHLIGHTER_SHARE = 0.02f

        /** Translucent so the text under it reads; the canvas strokes it once per path, as the laptop does. */
        const val HIGHLIGHTER_ALPHA = 0.35f

        /** An arrow head's wings are this many pen widths long, a little under 30 degrees off the shaft. */
        private const val HEAD_WIDTHS = 4f
        private const val HEAD_ANGLE = 0.5f

        fun widthShare(tool: PenTool): Float = if (tool == PenTool.HIGHLIGHTER) HIGHLIGHTER_SHARE else PEN_SHARE

        /**
         * The outer ends of an arrow head as x1, y1, x2, y2, meeting the shaft at [tipX], [tipY]. Null when the
         * arrow has no length yet, which gives it no direction to point in.
         */
        fun arrowWings(
            fromX: Float,
            fromY: Float,
            tipX: Float,
            tipY: Float,
            penWidth: Float,
        ): FloatArray? {
            val length = hypot(fromX - tipX, fromY - tipY)
            if (length == 0f) return null
            val scale = HEAD_WIDTHS * penWidth / length
            val bx = (fromX - tipX) * scale
            val by = (fromY - tipY) * scale
            val c = cos(HEAD_ANGLE)
            val s = sin(HEAD_ANGLE)
            return floatArrayOf(
                tipX + bx * c - by * s,
                tipY + bx * s + by * c,
                tipX + bx * c + by * s,
                tipY - bx * s + by * c,
            )
        }
    }
}
