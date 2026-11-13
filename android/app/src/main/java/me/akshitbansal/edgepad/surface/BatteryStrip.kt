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
import me.akshitbansal.edgepad.Type
import kotlin.math.roundToInt

/**
 * The row of battery chips above the top buttons: the laptop in use, this phone, and +N for everything else,
 * each one left out when it has nothing to show. A chip is a pill whose track fills to the level; low is red,
 * charging fills in the accent and wears a bolt.
 *
 * [refresh] is the only place that allocates: it remakes the labels, fits the text and writes [summary].
 * [layout] and [refresh] place every rect and every drawable's bounds, so [draw] only paints.
 *
 * The strip is [WIDTH_DP] wide however many chips it holds, and centred on the top edge, so the buttons'
 * keep-out in [ControlSurface.onSizeChanged] is sized off that constant: the corner dials' rulers and a split
 * corner's text stop short of it in both orientations.
 */
class BatteryStrip(
    private val context: Context,
    private val ink: Int,
    accent: Int,
    faint: Int,
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
        val box = RectF()
        var textX = 0f
    }

    private val chips = Array(MAX_CHIPS) { Chip(icon(R.drawable.ic_battery_bolt, accent)) }
    private var count = 0
    private var centreX = 0f

    /** How far below its own place a display cutout pushed the strip, in px; see [layout]. */
    private var shift = 0f
    private var baseline = 0f
    private val laptopIcon = icon(R.drawable.ic_battery_laptop, ink)
    private val phoneIcon = icon(R.drawable.ic_battery_phone, ink)

    private val track = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = faint }
    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val normalFill = withAlpha(ink, FILL_ALPHA)
    private val chargeFill = withAlpha(accent, FILL_ALPHA)
    private val lowFill = withAlpha(danger, FILL_ALPHA)
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
    ) {
        centreX = width / 2f
        this.shift = shift
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
    ): Float {
        val half = dp(WIDTH_DP) / 2
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
            val box = chip.box
            val radius = box.height() / 2
            canvas.drawRoundRect(box, radius, radius, track)
            if (chip.level > 0) {
                fill.color =
                    when {
                        chip.low -> lowFill
                        chip.charging -> chargeFill
                        else -> normalFill
                    }
                canvas.save()
                canvas.clipRect(box.left, box.top, box.left + box.width() * chip.level / FULL, box.bottom)
                canvas.drawRoundRect(box, radius, radius, fill)
                canvas.restore()
            }
            chip.icon?.draw(canvas)
            if (chip.charging) chip.bolt.draw(canvas)
            text.color = if (chip.low) danger else ink
            canvas.drawText(chip.label, chip.textX, baseline, text)
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
        chip.level = row.level.coerceIn(0, FULL.toInt())
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

    /** The text room a chip leaves once its padding, icon and bolt are taken. */
    private fun room(chip: Chip): Float {
        var room = dp(CHIP_DP - 2 * PAD_DP)
        if (chip.icon != null) room -= dp(ICON_DP + GAP_IN_DP)
        if (chip.charging) room -= dp(BOLT_DP + GAP_IN_DP)
        return room
    }

    /** A large font scale shrinks the labels to fit their fixed chips rather than spilling out of them. */
    private fun fitText() {
        text.textSize = baseTextSize
        var scale = 1f
        for (i in 0 until count) {
            val width = text.measureText(chips[i].label)
            if (width > 0f) scale = minOf(scale, room(chips[i]) / width)
        }
        text.textSize = baseTextSize * scale
    }

    private fun place() {
        if (count == 0) {
            hit.setEmpty()
            return
        }
        val chipWidth = dp(CHIP_DP)
        val gap = dp(GAP_DP)
        val span = count * chipWidth + (count - 1) * gap
        val cy = dp(CENTRE_DP) + shift
        val half = dp(HEIGHT_DP) / 2
        baseline = cy + text.textSize * Type.CAP_CENTRE
        var left = centreX - span / 2
        for (i in 0 until count) {
            val chip = chips[i]
            chip.box.set(left, cy - half, left + chipWidth, cy + half)
            val labelWidth = text.measureText(chip.label)
            val icon = chip.icon
            var x =
                if (icon == null) {
                    // The +N chip: its label alone, centred.
                    chip.box.centerX() - labelWidth / 2
                } else {
                    var content = dp(ICON_DP + GAP_IN_DP) + labelWidth
                    if (chip.charging) content += dp(GAP_IN_DP + BOLT_DP)
                    chip.box.centerX() - content / 2
                }
            if (icon != null) {
                bounds(icon, x, cy, dp(ICON_DP))
                x += dp(ICON_DP + GAP_IN_DP)
            }
            chip.textX = x
            if (chip.charging) bounds(chip.bolt, x + labelWidth + dp(GAP_IN_DP), cy, dp(BOLT_DP))
            left += chipWidth + gap
        }
        hit.set(centreX - span / 2, dp(HIT_TOP_DP) + shift, centreX + span / 2, dp(HIT_BOTTOM_DP) + shift)
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

    private fun withAlpha(
        color: Int,
        fraction: Float,
    ): Int = color and RGB_MASK or ((fraction * MAX_ALPHA).roundToInt() shl ALPHA_SHIFT)

    companion object {
        /** Laptop, phone, +N. */
        private const val MAX_CHIPS = 3
        const val CHIP_DP = 44f
        const val GAP_DP = 6f

        /** The strip's width whatever it holds: three chips and the two gaps between them. */
        const val WIDTH_DP = MAX_CHIPS * CHIP_DP + (MAX_CHIPS - 1) * GAP_DP

        /**
         * The chips' centre and height, and the strip's tap rect. The rect ends at 36dp, where the top buttons'
         * own rects begin ([ControlSurface.TOP_DP] less half of [me.akshitbansal.edgepad.Space.TOUCH]).
         */
        const val CENTRE_DP = 22f
        const val HEIGHT_DP = 24f
        const val HIT_TOP_DP = 6f
        const val HIT_BOTTOM_DP = 36f
        private const val PAD_DP = 4f
        private const val ICON_DP = 12f
        private const val BOLT_DP = 9f
        private const val GAP_IN_DP = 2f
        private const val TEXT_SP = 10f
        private const val FILL_ALPHA = 0.28f
        private const val FULL = 100f
        private const val RGB_MASK = 0x00FFFFFF
        private const val MAX_ALPHA = 255
        private const val ALPHA_SHIFT = 24
    }
}
