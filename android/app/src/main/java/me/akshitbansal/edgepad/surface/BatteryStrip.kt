package me.akshitbansal.edgepad.surface

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.RectF
import android.graphics.drawable.Drawable
import android.text.TextPaint
import android.util.TypedValue
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.drawFitted
import kotlin.math.roundToInt

/**
 * The row of batteries above the top buttons: the laptop in use, this phone, and +N for everything else, each one
 * left out when it has nothing to show. A device is its icon, a battery outline filled to the level, the percent
 * and, when charging, a bolt. Low is red, charging fills in the accent. No underline and no scrim: the shapes sit
 * straight on the surface.
 *
 * [refresh] is the only place that allocates: it remakes the labels, fits the text and writes [summary].
 * [layout] and [refresh] place every rect and every drawable's bounds, so [draw] only paints.
 *
 * The strip is never wider than the button row under it ([roomDp]) and is centred on the top edge, so the
 * buttons' keep-out in [ControlSurface.onSizeChanged] covers it: the corner dials' rulers and a split corner's
 * text stop short of it in both orientations. Past that width the text shrinks, then the icons go, then the
 * text is cut with an ellipsis.
 */
class BatteryStrip(
    private val context: Context,
    private val ink: Int,
    private val accent: Int,
    private val dim: Int,
    // The theme's red, never the user's control colour: low has to read as low on any backdrop.
    private val danger: Int,
) {
    private val metrics = context.resources.displayMetrics
    private val density = metrics.density

    private class Chip(
        val bolt: Drawable,
    ) {
        var label = ""
        var icon: Drawable? = null

        /** 0-100, or -1 for the +N chip, which has no level. */
        var level = -1
        var charging = false
        var low = false
        val body = RectF()
        val lit = RectF()
        val nub = RectF()
        var textX = 0f
        var textWidth = 0f
        var width = 0f
    }

    private val chips = Array(MAX_CHIPS) { Chip(icon(R.drawable.ic_battery_bolt, accent)) }
    private var count = 0

    /** False when the text would otherwise shrink under [FLOOR_SP]: the device icons are then left out. */
    private var iconsShown = true
    private var centreX = 0f

    /** The most width the strip may take, in px. */
    private var roomPx = 0f

    /** How far below its own place a display cutout pushed the strip, in px; see [layout]. */
    private var shift = 0f
    private var baseline = 0f
    private val laptopIcon = icon(R.drawable.ic_battery_laptop, dim)
    private val phoneIcon = icon(R.drawable.ic_battery_phone, dim)
    private val widths = FloatArray(MAX_CHIPS)

    private val outline =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = dp(BatteryShape.STROKE_DP)
            color = dim
        }
    private val fillRadius = dp(BatteryShape.FILL_CORNER_DP)
    private val nubPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = dim }
    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val baseTextSize = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, TEXT_SP, metrics)
    private val text =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.bold
            textSize = baseTextSize
        }

    /** Where a tap opens the batteries; empty when there are no chips. */
    val hit = RectF()

    /** The strip's accessibility action label, empty when there are no chips. Never announced on change. */
    var summary = ""
        private set

    /**
     * The view's size changed; the strip stays centred on it, [shift] px below its own place, which is how far a
     * display cutout at the top edge pushes it ([cutoutShift]).
     */
    fun layout(
        width: Int,
        shift: Float,
        room: Float,
    ) {
        centreX = width / 2f
        this.shift = shift
        roomPx = room
        if (count > 0) fitText()
        place()
    }

    /**
     * How far down the strip must move, in px, to clear the cutouts in [rects] (a punch-hole or notch over the
     * top edge's middle): its top goes to the lowest one's bottom plus [gap]. Zero when none overlaps it, as in
     * landscape, where the cutout sits on a side edge.
     */
    fun cutoutShift(
        width: Int,
        rects: List<Rect>,
        gap: Float,
        room: Float,
    ): Float {
        val half = room / 2
        val top = dp(CENTRE_DP - HEIGHT_DP / 2)
        val bottom = dp(HIT_BOTTOM_DP)
        var shift = 0f
        for (rect in rects) {
            val across = rect.left < width / 2f + half && rect.right > width / 2f - half
            if (across && rect.top < bottom) shift = maxOf(shift, rect.bottom + gap - top)
        }
        return shift
    }

    fun refresh(model: BatteryChips) {
        count = 0
        val spoken = ArrayList<String>(MAX_CHIPS)
        model.laptop?.let { spoken.add(fillChip(chips[count++], it, laptopIcon)) }
        model.phone?.let { spoken.add(fillChip(chips[count++], it, phoneIcon)) }
        if (model.plus > 0) {
            val chip = chips[count++]
            chip.label = context.getString(R.string.battery_more, model.plus)
            chip.icon = null
            chip.level = -1
            chip.charging = false
            chip.low = model.plusLow
            spoken.add(if (chip.low) context.getString(R.string.battery_more_low, chip.label) else chip.label)
        }
        summary =
            if (count == 0) {
                ""
            } else {
                val list = spoken.joinToString(context.getString(R.string.list_separator))
                context.getString(R.string.battery_summary, list)
            }
        fitText()
        place()
    }

    fun draw(canvas: Canvas) {
        for (i in 0 until count) {
            val chip = chips[i]
            if (chip.level >= 0) {
                canvas.drawRoundRect(chip.body, dp(BODY_RADIUS_DP), dp(BODY_RADIUS_DP), outline)
                canvas.drawRoundRect(chip.nub, dp(NUB_RADIUS_DP), dp(NUB_RADIUS_DP), nubPaint)
                if (chip.level > 0) {
                    fill.color = BatteryShape.fillColor(chip.low, chip.charging, danger, accent, ink)
                    canvas.drawRoundRect(chip.lit, fillRadius, fillRadius, fill)
                }
            }
            if (iconsShown) chip.icon?.draw(canvas)
            if (chip.charging) chip.bolt.draw(canvas)
            text.color =
                when {
                    chip.low -> danger
                    chip.icon == null -> dim
                    else -> ink
                }
            text.drawFitted(canvas, chip.label, chip.textX, baseline, chip.textWidth + HAIR_PX, 1f)
        }
    }

    /** Fills [chip] from [row] and returns what is said for it. */
    private fun fillChip(
        chip: Chip,
        row: BatteryRow,
        icon: Drawable,
    ): String {
        chip.label = context.getString(R.string.battery_percent, row.level)
        chip.icon = icon
        chip.level = row.level.coerceIn(0, FULL)
        chip.charging = row.charging
        chip.low = row.low
        // One string per state, so a language can order the name, the level and the state its own way.
        val spoken =
            when {
                row.charging -> R.string.battery_spoken_charging
                row.low -> R.string.battery_spoken_low
                else -> R.string.battery_spoken
            }
        return context.getString(spoken, row.name, chip.label)
    }

    private fun fixedOf(chip: Chip): Float =
        BatteryShape.fixedWidth(chip.icon != null, chip.icon != null && iconsShown, chip.charging)

    /** A large font scale shrinks the labels to fit the strip rather than spilling out of it. */
    private fun fitText() {
        text.textSize = baseTextSize
        val floor = minOf(1f, TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, FLOOR_SP, metrics) / baseTextSize)
        // Not under the floor: the device icons go first, and what room that frees is what the text then gets.
        iconsShown = true
        if (StripFit.dropsIcons(neededScale(), floor)) iconsShown = false
        text.textSize = baseTextSize * StripFit.scale(neededScale(), floor)
    }

    /** The scale at which the whole strip fits its room, from 1 down to whatever is needed, measured at base size. */
    private fun neededScale(): Float {
        text.textSize = baseTextSize
        var fixed = (count - 1).coerceAtLeast(0) * BatteryShape.SPACING_DP
        var labels = 0f
        for (i in 0 until count) {
            fixed += fixedOf(chips[i])
            labels += text.measureText(chips[i].label) / density
        }
        return BatteryShape.neededScale(fixed, labels, roomPx / density)
    }

    private fun place() {
        if (count == 0) {
            hit.setEmpty()
            return
        }
        // Past the floor the text cannot shrink any more, so each label gets its share of what is left and is cut.
        var fixed = dp((count - 1) * BatteryShape.SPACING_DP)
        var labels = 0f
        for (i in 0 until count) {
            chips[i].textWidth = text.measureText(chips[i].label)
            fixed += dp(fixedOf(chips[i]))
            labels += chips[i].textWidth
        }
        val squeeze = if (labels > 0f) ((roomPx - fixed) / labels).coerceIn(0f, 1f) else 1f
        for (i in 0 until count) {
            val chip = chips[i]
            chip.textWidth *= squeeze
            chip.width = dp(fixedOf(chip)) + chip.textWidth
            widths[i] = chip.width / density
        }
        val span = dp(BatteryShape.span(widths, count))
        val cy = dp(CENTRE_DP) + shift
        baseline = cy + text.textSize * Type.CAP_CENTRE
        var left = centreX - span / 2
        for (i in 0 until count) {
            val chip = chips[i]
            var x = left
            val icon = chip.icon?.takeIf { iconsShown }
            if (icon != null) {
                bounds(icon, x, cy, dp(BatteryShape.ICON_DP))
                x += dp(BatteryShape.ICON_DP + BatteryShape.GAP_DP)
            }
            if (chip.level >= 0) {
                val half = dp(BatteryShape.BODY_H_DP) / 2
                val stroke = dp(BatteryShape.STROKE_DP) / 2
                // The stroke is centred on its path, so the path is inset by half of it to keep the outer 22 x 11.
                val right = x + dp(BatteryShape.BODY_W_DP) - stroke
                chip.body.set(x + stroke, cy - half + stroke, right, cy + half - stroke)
                val inset = dp(BatteryShape.STROKE_DP + BatteryShape.INNER_PAD_DP)
                val fillHalf = dp(BatteryShape.fillHeight()) / 2
                val litRight = x + inset + dp(BatteryShape.fillWidth(chip.level))
                chip.lit.set(x + inset, cy - fillHalf, litRight, cy + fillHalf)
                val nubLeft = x + dp(BatteryShape.BODY_W_DP + BatteryShape.NUB_GAP_DP)
                val nubHalf = dp(BatteryShape.NUB_H_DP) / 2
                chip.nub.set(nubLeft, cy - nubHalf, nubLeft + dp(BatteryShape.NUB_W_DP), cy + nubHalf)
                x = nubLeft + dp(BatteryShape.NUB_W_DP + BatteryShape.GAP_DP)
            }
            chip.textX = x
            if (chip.charging) {
                val boltLeft = x + chip.textWidth + dp(BatteryShape.GAP_DP)
                bounds(chip.bolt, boltLeft, cy, dp(BatteryShape.BOLT_DP))
            }
            left += chip.width + dp(BatteryShape.SPACING_DP)
        }
        val pad = dp(Space.S)
        hit.set(centreX - span / 2 - pad, dp(HIT_TOP_DP) + shift, centreX + span / 2 + pad, dp(HIT_BOTTOM_DP) + shift)
    }

    private fun bounds(
        icon: Drawable,
        left: Float,
        cy: Float,
        size: Float,
    ) {
        val top = (cy - size / 2).roundToInt()
        val l = left.roundToInt()
        icon.setBounds(l, top, l + size.roundToInt(), top + size.roundToInt())
    }

    private fun icon(
        id: Int,
        color: Int,
    ): Drawable = checkNotNull(context.getDrawable(id)).mutate().apply { setTint(color) }

    private fun dp(value: Float): Float = value * density

    companion object {
        /** Laptop, phone, +N. */
        private const val MAX_CHIPS = 3

        /**
         * The strip's room, in dp, for a button row reaching [reachDp] either side of the centre: the row's own width,
         * so the row's keep-out zone covers the strip. Never under [MIN_ROOM_DP].
         */
        fun roomDp(reachDp: Float): Float = maxOf(MIN_ROOM_DP, 2 * (reachDp + Space.TOUCH / 2))

        /** The upright row's width, 1.5 steps of 48dp and half a touch target either side. */
        const val MIN_ROOM_DP = 192f

        /**
         * The strip's centre, its height and its tap rect. The shapes end at 32dp, above where the top buttons'
         * own rects begin ([ControlSurface.TOP_DP] less half of [me.akshitbansal.edgepad.Space.TOUCH]); the rect is
         * [me.akshitbansal.edgepad.Space.TOUCH] tall, runs into the buttons' top and loses those taps to them.
         */
        const val CENTRE_DP = 19f
        const val HEIGHT_DP = 26f
        const val HIT_TOP_DP = 0f
        const val HIT_BOTTOM_DP = 48f
        private const val HAIR_PX = 0.5f
        private const val TEXT_SP = 13f

        /** The smallest the text is drawn at, however large the system font. */
        private const val FLOOR_SP = 9f
        private const val FULL = 100

        /** The outline is stroked on a path inset by half the stroke, so its radii shrink by the same. */
        private const val BODY_RADIUS_DP = BatteryShape.CORNER_DP - BatteryShape.STROKE_DP / 2
        private const val NUB_RADIUS_DP = 1f
    }
}
