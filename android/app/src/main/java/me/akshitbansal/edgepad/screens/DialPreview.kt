package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.view.View
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.surface.Dial
import me.akshitbansal.edgepad.surface.Perimeter
import me.akshitbansal.edgepad.surface.RulerPainter

/**
 * A live corner of the control surface, drawn with the same painter at the length and height the sliders
 * hold, so a slide shows its effect at once. Shown at true size: what fits in the box is what fits on the
 * surface.
 */
class DialPreview(
    context: Context,
    settings: Settings,
) : View(context) {
    /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
    constructor(context: Context) : this(context, Settings(context))

    private val density = resources.displayMetrics.density
    private val palette = Palette.of(context)
    private val label = context.getString(R.string.dial_volume_short)
    private val painter =
        RulerPainter(resources.displayMetrics, settings.dialLength, settings.dialHeight)
    private val unitsPerDp = Dial.BASE_UNITS_PER_DP * settings.sensitivity
    private var perimeter = Perimeter(1f, 1f, 1f)
    private val pt = FloatArray(4)
    private val border =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = Space.HAIR * density
            color = palette.line
        }

    init {
        contentDescription = context.getString(R.string.dial_preview_description)
    }

    fun show(
        lengthDp: Float,
        height: Float,
    ) {
        painter.halfLengthDp = lengthDp
        painter.height = height
        invalidate()
    }

    override fun onSizeChanged(
        w: Int,
        h: Int,
        oldw: Int,
        oldh: Int,
    ) {
        super.onSizeChanged(w, h, oldw, oldh)
        // The box stands in for the phone's top-left corner, so the ruler wraps its own top-left bend.
        perimeter = Perimeter(w.toFloat(), h.toFloat(), BEND_DP * density)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        canvas.drawColor(palette.background)
        canvas.drawRect(
            border.strokeWidth / 2,
            border.strokeWidth / 2,
            width - border.strokeWidth / 2,
            height - border.strokeWidth / 2,
            border,
        )
        painter.draw(
            canvas,
            perimeter,
            perimeter.lengthAt(0f),
            painter.halfLengthDp * density,
            painter.halfLengthDp * density,
            LEVEL / unitsPerDp,
            Dial.MAX_LEVEL / unitsPerDp,
            false,
            palette.ink,
            palette.dim,
            label,
            LEVEL.toInt().toString(),
            pt,
        )
    }

    private companion object {
        const val BEND_DP = 24f
        const val LEVEL = 50f
    }
}
