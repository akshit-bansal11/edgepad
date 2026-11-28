package me.akshitbansal.edgepad.screens

import android.graphics.Canvas
import android.graphics.ColorFilter
import android.graphics.Paint
import android.graphics.PixelFormat
import android.graphics.drawable.Drawable

/**
 * A tiny phone outline with the short stretch of edge where one dial sits drawn in the accent, so the Corners page
 * shows where each row's dial is. [slot] is one of the eight places a dial can sit, clockwise from the left edge of
 * the top left corner ([me.akshitbansal.edgepad.surface.CornerSlots]); a plain corner's dial sits in the slot it
 * inherits, so the four plain corners are four of the eight.
 *
 * One stroke width for the outline and the stretch, both with round caps. The paints are built once, because lint
 * rejects allocating while drawing.
 */
class CornerMarker(
    outline: Int,
    accent: Int,
    private val slot: Int,
    stroke: Float,
) : Drawable() {
    private val outlinePaint = strokePaint(outline, stroke)
    private val accentPaint = strokePaint(accent, stroke)
    private val half = stroke / 2f

    override fun draw(canvas: Canvas) {
        val b = bounds
        val height = b.height() - 2f * half
        val width = height * WIDTH_RATIO
        val left = b.exactCenterX() - width / 2f
        val top = b.top + half
        val right = left + width
        val bottom = top + height
        val radius = width * RADIUS_RATIO
        canvas.drawRoundRect(left, top, right, bottom, radius, radius, outlinePaint)
        // A side stretch starts a corner's radius in from the corner and runs a part of the straight edge.
        val side = (height - 2f * radius) * RUN_RATIO
        val flat = (width - 2f * radius) * RUN_RATIO
        when (slot) {
            0 -> canvas.drawLine(left, top + radius, left, top + radius + side, accentPaint)
            1 -> canvas.drawLine(left + radius, top, left + radius + flat, top, accentPaint)
            2 -> canvas.drawLine(right - radius - flat, top, right - radius, top, accentPaint)
            3 -> canvas.drawLine(right, top + radius, right, top + radius + side, accentPaint)
            4 -> canvas.drawLine(right, bottom - radius - side, right, bottom - radius, accentPaint)
            5 -> canvas.drawLine(right - radius - flat, bottom, right - radius, bottom, accentPaint)
            6 -> canvas.drawLine(left + radius, bottom, left + radius + flat, bottom, accentPaint)
            else -> canvas.drawLine(left, bottom - radius - side, left, bottom - radius, accentPaint)
        }
    }

    override fun setAlpha(alpha: Int) {
        outlinePaint.alpha = alpha
        accentPaint.alpha = alpha
        invalidateSelf()
    }

    override fun setColorFilter(colorFilter: ColorFilter?) {
        outlinePaint.colorFilter = colorFilter
        accentPaint.colorFilter = colorFilter
        invalidateSelf()
    }

    @Deprecated("Deprecated in Java")
    override fun getOpacity(): Int = PixelFormat.TRANSLUCENT

    private fun strokePaint(
        color: Int,
        width: Float,
    ): Paint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            this.color = color
            style = Paint.Style.STROKE
            strokeWidth = width
            strokeCap = Paint.Cap.ROUND
        }

    private companion object {
        const val WIDTH_RATIO = 0.6f
        const val RADIUS_RATIO = 0.22f
        const val RUN_RATIO = 0.55f
    }
}
