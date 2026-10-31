package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.PenColor
import me.akshitbansal.edgepad.protocol.TextKind
import java.util.Locale
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.roundToInt
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

/**
 * One finger-down to finger-up. A shape keeps the down point and the latest one, since that is all it is. [rgb] is
 * 0xRRGGBB, [width] the wire's 1 to 100 and [opacity] its 10 to 100 percent, each as they were when the finger went down.
 */
class Stroke(
    val tool: PenTool,
    val rgb: Int,
    val width: Int,
    val opacity: Int,
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
class Strokes(
    rgb: Int = DEFAULT_RGB,
    width: Int = DEFAULT_WIDTH,
    opacity: Int = DEFAULT_OPACITY,
) {
    private val done = ArrayList<Stroke>()

    var tool = PenTool.PEN
        private set

    /** The colour of the next stroke, 0xRRGGBB: one of the palette or one the user picked. */
    var rgb = rgb and RGB_MASK
        private set

    /** The width of the next stroke on the wire's scale, [MIN_WIDTH] to [MAX_WIDTH]. */
    var width = width.coerceIn(MIN_WIDTH, MAX_WIDTH)
        private set

    /** The opacity of the next stroke in percent, [MIN_OPACITY] to [MAX_OPACITY]. */
    var opacity = opacity.coerceIn(MIN_OPACITY, MAX_OPACITY)
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
        rgb = PenColor.entries[index].rgb
        return true
    }

    /** Any colour, which the laptop gets as six hex digits; false for a number that is not 0xRRGGBB. */
    fun selectRgb(rgb: Int): Boolean {
        if (rgb != rgb and RGB_MASK) return false
        this.rgb = rgb
        return true
    }

    /** False for a width the laptop would drop, and nothing changes. */
    fun selectWidth(width: Int): Boolean {
        if (width !in 1..MAX_WIDTH) return false
        this.width = width.coerceAtLeast(MIN_WIDTH)
        return true
    }

    /** False for an opacity the laptop would drop, and nothing changes. */
    fun selectOpacity(opacity: Int): Boolean {
        if (opacity !in MIN_OPACITY..MAX_OPACITY) return false
        this.opacity = opacity
        return true
    }

    /**
     * What a laptop is told about the pen: the colour, the width and the opacity. It forgets all three when its
     * session ends, so these go out whenever a surface opens, as well as when one of them changes.
     */
    fun styleFrames(): List<Frame> =
        listOf(colourFrame(rgb), ControlId.PEN_WIDTH.set(width), ControlId.PEN_OPACITY.set(opacity))

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
                active = Stroke(tool, rgb, width, opacity).also { it.points.add(point) }
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

        private const val RGB_MASK = 0xFFFFFF

        const val DEFAULT_RGB = 0x000000

        /** 20 steps is 0.4% of the screen's height, the pen's width before it could be chosen. */
        const val DEFAULT_WIDTH = 20

        /** Below 5 the laptop draws the same 0.1% hairline, so the slider starts here. */
        const val MIN_WIDTH = 5
        const val MAX_WIDTH = 100

        /** The slider moves a size at a time, each one worth this many width steps, so it has 20 positions and not 96. */
        const val WIDTH_PER_SIZE = 5
        const val DEFAULT_OPACITY = 100
        const val MIN_OPACITY = 10
        const val MAX_OPACITY = 100

        /** The opacity slider moves this many percent a step. */
        const val OPACITY_STEP = 5

        /** One width step is 0.02% of the screen's height, written as a divisor so 20 steps are exactly 0.004. */
        private const val WIDTH_DIVISOR = 5000f

        /** The highlighter is this many times as wide as the pen is set to be, as on the laptop. */
        const val HIGHLIGHTER_WIDTHS = 5f

        /** Translucent so the text under it reads; the canvas strokes it once per path, as the laptop does. */
        const val HIGHLIGHTER_ALPHA = 0.35f

        private const val OPAQUE_ALPHA = 255f
        private const val PERCENT = 100f

        /** An arrow head's wings are this many pen widths long, a little under 30 degrees off the shaft. */
        private const val HEAD_WIDTHS = 4f
        private const val HEAD_ANGLE = 0.5f

        /** The pen's width as a share of the screen's height, 0.001 to 0.02, for a wire [width]. */
        fun penShare(width: Int): Float = maxOf(MIN_WIDTH, width) / WIDTH_DIVISOR

        fun widthShare(
            tool: PenTool,
            width: Int = DEFAULT_WIDTH,
        ): Float = penShare(width) * if (tool == PenTool.HIGHLIGHTER) HIGHLIGHTER_WIDTHS else 1f

        /** The ink's alpha, 0 to 255: the opacity the user set, and for the highlighter that of its 35%. */
        fun alpha(
            tool: PenTool,
            opacity: Int,
        ): Int {
            val own = if (tool == PenTool.HIGHLIGHTER) HIGHLIGHTER_ALPHA else 1f
            return (OPAQUE_ALPHA * own * opacity / PERCENT).roundToInt()
        }

        /**
         * How the laptop is told a colour: by index when it is one of the eight, which is the cheaper frame, and as six
         * hex digits otherwise. Either way the laptop draws the same colour.
         */
        fun colourFrame(rgb: Int): Frame {
            val stock = PenColor.entries.firstOrNull { it.rgb == rgb }
            return if (stock != null) {
                ControlId.PEN_COLOR.set(stock.index)
            } else {
                TextKind.PEN_RGB.frame("%06X".format(Locale.ROOT, rgb and RGB_MASK))
            }
        }

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
