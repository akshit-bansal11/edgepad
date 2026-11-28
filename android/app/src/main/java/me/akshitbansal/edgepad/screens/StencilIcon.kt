package me.akshitbansal.edgepad.screens

import android.graphics.Canvas
import android.graphics.ColorFilter
import android.graphics.Paint
import android.graphics.Path
import android.graphics.PixelFormat
import android.graphics.RectF
import android.graphics.drawable.Drawable

/**
 * The Controls tiles' icons: a white (the palette's ink) outline with one detail in the accent. Every line is
 * [STROKE] units wide with round caps and joins, nothing is filled but the accent when it is a dot, and nothing is
 * cut out, so the tile behind shows through whatever its state.
 *
 * The shapes sit on a 48-unit grid (a 24-unit grid doubled), scaled to the bounds, inside a 4-unit margin, with the
 * lines kept a whole [STROKE] apart where they run side by side. They are built once here, because lint rejects
 * allocating while drawing; [draw] only replays them.
 */
class StencilIcon(
    kind: Kind,
    ink: Int,
    accent: Int,
) : Drawable() {
    enum class Kind {
        KEYBOARD,
        GAMEPAD,
        MACROS,
        CAPTURE,
        ANNOTATE,
        CLIPBOARD,
        PRESENTER,
        SYSTEM,
        AI,
        GITHUB,
        SEND,
        DEVELOPER,
        POWER,
    }

    /** The outline, the one accent line, and the accent dot a few icons carry instead of or beside it. */
    private val outline = Path()
    private val detail = Path()
    private val dot = Path()

    private val inkPaint = strokePaint(ink)
    private val accentPaint = strokePaint(accent)
    private val dotPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = accent }

    /** What the icon is composed into, so [setAlpha] and [setColorFilter] reach every part as one. */
    private val layer = Paint()

    init {
        when (kind) {
            Kind.KEYBOARD -> {
                outline.card(5f, 12f, 38f, 24f, 5f)
                for (y in listOf(18f, 24f)) {
                    outline.line(12f, y, 16f, y)
                    outline.line(22f, y, 26f, y)
                    outline.line(32f, y, 36f, y)
                }
                detail.line(16f, 30f, 32f, 30f)
            }

            Kind.GAMEPAD -> {
                outline.card(4f, 15f, 40f, 21f, 10.5f)
                outline.line(12f, 9f, 18f, 9f)
                outline.line(30f, 9f, 36f, 9f)
                outline.addCircle(14f, 25.5f, 3.5f, Path.Direction.CW)
                dot.addCircle(34f, 25.5f, DOT, Path.Direction.CW)
            }

            Kind.MACROS -> {
                // A card with a second one behind it, drawn only where it shows, and a play triangle on the front.
                outline.card(6f, 14f, 28f, 28f, 6f)
                outline.moveTo(14f, 8f)
                outline.lineTo(34f, 8f)
                outline.quadTo(40f, 8f, 40f, 14f)
                outline.lineTo(40f, 30f)
                detail.moveTo(15f, 22f)
                detail.lineTo(25f, 28f)
                detail.lineTo(15f, 34f)
                detail.close()
            }

            Kind.CAPTURE -> {
                // A viewfinder: four corners round the frame and a lens in the middle.
                outline.moveTo(6f, 16f)
                outline.lineTo(6f, 12f)
                outline.quadTo(6f, 6f, 12f, 6f)
                outline.lineTo(16f, 6f)
                outline.moveTo(32f, 6f)
                outline.lineTo(36f, 6f)
                outline.quadTo(42f, 6f, 42f, 12f)
                outline.lineTo(42f, 16f)
                outline.moveTo(42f, 32f)
                outline.lineTo(42f, 36f)
                outline.quadTo(42f, 42f, 36f, 42f)
                outline.lineTo(32f, 42f)
                outline.moveTo(16f, 42f)
                outline.lineTo(12f, 42f)
                outline.quadTo(6f, 42f, 6f, 36f)
                outline.lineTo(6f, 32f)
                detail.addCircle(24f, 24f, 5f, Path.Direction.CW)
            }

            Kind.ANNOTATE -> {
                // A pen on the diagonal, its tip at the lower left, and the line it draws.
                outline.moveTo(9f, 39f)
                outline.lineTo(17.5f, 36.1f)
                outline.lineTo(31.6f, 22f)
                outline.lineTo(26f, 16.4f)
                outline.lineTo(11.9f, 30.5f)
                outline.close()
                outline.moveTo(11.9f, 30.5f)
                outline.lineTo(17.5f, 36.1f)
                detail.moveTo(25f, 40.4f)
                detail.cubicTo(30.2f, 37.2f, 34.2f, 42.2f, 41f, 38.4f)
            }

            Kind.CLIPBOARD -> {
                // The board's top edge is open where the clip, in the accent, closes it.
                outline.moveTo(18f, 10f)
                outline.lineTo(15f, 10f)
                outline.quadTo(10f, 10f, 10f, 15f)
                outline.lineTo(10f, 38f)
                outline.quadTo(10f, 43f, 15f, 43f)
                outline.lineTo(33f, 43f)
                outline.quadTo(38f, 43f, 38f, 38f)
                outline.lineTo(38f, 15f)
                outline.quadTo(38f, 10f, 33f, 10f)
                outline.lineTo(30f, 10f)
                outline.line(17f, 20f, 31f, 20f)
                outline.line(17f, 27f, 31f, 27f)
                outline.line(17f, 34f, 25f, 34f)
                detail.moveTo(18f, 10f)
                detail.lineTo(18f, 6f)
                detail.lineTo(30f, 6f)
                detail.lineTo(30f, 10f)
            }

            Kind.PRESENTER -> {
                // A screen on a stand, with the laser's dot on it.
                outline.card(5f, 7f, 38f, 24f, 4f)
                outline.moveTo(24f, 31f)
                outline.lineTo(24f, 37f)
                outline.lineTo(16f, 43f)
                outline.moveTo(24f, 37f)
                outline.lineTo(32f, 43f)
                dot.addCircle(30f, 19f, DOT, Path.Direction.CW)
            }

            Kind.SEND -> {
                // A paper plane heading up and right; the fold down its middle is the accent.
                outline.moveTo(6f, 22f)
                outline.lineTo(42f, 7f)
                outline.lineTo(27f, 42f)
                outline.lineTo(22f, 28f)
                outline.close()
                detail.line(42f, 7f, 22f, 28f)
            }

            Kind.SYSTEM -> {
                // A screen with a pulse on it that ends in the accent dot, on a stand.
                outline.card(4f, 7f, 40f, 28f, 5f)
                outline.moveTo(11f, 21f)
                outline.lineTo(17f, 21f)
                outline.lineTo(20f, 15f)
                outline.lineTo(25f, 27f)
                outline.lineTo(28f, 21f)
                outline.moveTo(17f, 41f)
                outline.lineTo(31f, 41f)
                outline.moveTo(24f, 35f)
                outline.lineTo(24f, 41f)
                dot.addCircle(36f, 21f, DOT, Path.Direction.CW)
            }

            Kind.DEVELOPER -> {
                // A screen with the brackets of </> and the accent slash between them.
                outline.card(4f, 8f, 40f, 32f, 5f)
                outline.moveTo(16f, 18f)
                outline.lineTo(10f, 24f)
                outline.lineTo(16f, 30f)
                outline.moveTo(32f, 18f)
                outline.lineTo(38f, 24f)
                outline.lineTo(32f, 30f)
                detail.line(26.5f, 17.5f, 21.5f, 30.5f)
            }

            Kind.AI -> {
                outline.addCircle(24f, 24f, 18f, Path.Direction.CW)
                outline.addCircle(24f, 24f, 10f, Path.Direction.CW)
                dot.addCircle(24f, 24f, 3.5f, Path.Direction.CW)
            }

            Kind.GITHUB -> {
                // A branch: two nodes joined by a line, and the accent node that leaves it.
                outline.addCircle(12f, 10f, NODE, Path.Direction.CW)
                outline.addCircle(12f, 38f, NODE, Path.Direction.CW)
                outline.line(12f, 14.5f, 12f, 33.5f)
                outline.moveTo(36f, 20.5f)
                outline.cubicTo(36f, 26f, 28f, 26f, 12f, 26f)
                detail.addCircle(36f, 16f, NODE, Path.Direction.CW)
            }

            Kind.POWER -> {
                // The power ring, open at the top, with the accent bar through the gap.
                outline.arcTo(RectF(10f, 12f, 38f, 40f), 300f, 300f, true)
                detail.line(24f, 7f, 24f, 24f)
            }
        }
    }

    override fun draw(canvas: Canvas) {
        val box = bounds
        if (box.isEmpty) return
        val saved = canvas.save()
        canvas.translate(box.left.toFloat(), box.top.toFloat())
        canvas.scale(box.width() / SIZE, box.height() / SIZE)
        canvas.saveLayer(0f, 0f, SIZE, SIZE, layer)
        canvas.drawPath(outline, inkPaint)
        canvas.drawPath(detail, accentPaint)
        canvas.drawPath(dot, dotPaint)
        canvas.restoreToCount(saved)
    }

    override fun setAlpha(alpha: Int) {
        layer.alpha = alpha
        invalidateSelf()
    }

    override fun setColorFilter(colorFilter: ColorFilter?) {
        layer.colorFilter = colorFilter
        invalidateSelf()
    }

    @Deprecated("Deprecated in Java")
    override fun getOpacity(): Int = PixelFormat.TRANSLUCENT

    override fun getIntrinsicWidth(): Int = SIZE.toInt()

    override fun getIntrinsicHeight(): Int = SIZE.toInt()

    private fun Path.card(
        x: Float,
        y: Float,
        width: Float,
        height: Float,
        radius: Float,
    ) = addRoundRect(x, y, x + width, y + height, radius, radius, Path.Direction.CW)

    private fun Path.line(
        x1: Float,
        y1: Float,
        x2: Float,
        y2: Float,
    ) {
        moveTo(x1, y1)
        lineTo(x2, y2)
    }

    private fun strokePaint(color: Int): Paint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            this.color = color
            style = Paint.Style.STROKE
            strokeWidth = STROKE
            strokeCap = Paint.Cap.ROUND
            strokeJoin = Paint.Join.ROUND
        }

    private companion object {
        const val SIZE = 48f

        /** The one line weight, in grid units, of every icon. */
        const val STROKE = 3f

        /** The radius of an accent dot, and of a node in the branch. */
        const val DOT = 2.25f
        const val NODE = 4.5f
    }
}
