package me.akshitbansal.edgepad.screens

import android.graphics.Canvas
import android.graphics.ColorFilter
import android.graphics.Paint
import android.graphics.Path
import android.graphics.PixelFormat
import android.graphics.PorterDuff
import android.graphics.PorterDuffXfermode
import android.graphics.drawable.Drawable

/**
 * The Controls tiles' icons, in three colours with true cut-outs: [ink] for the body, [accent] for the one
 * live part, and holes through the body, so whatever the tile is painted shows through them in any state. That
 * rules out a vector drawable, whose colours are fixed in XML, and the palette is only known at run time.
 *
 * The shapes are drawn in a 48-unit square scaled to the bounds. They are built once here, because lint rejects
 * allocating while drawing; [draw] only replays them.
 */
class StencilIcon(
    kind: Kind,
    ink: Int,
    accent: Int,
) : Drawable() {
    enum class Kind { KEYBOARD, GAMEPAD, MACROS, CAPTURE, ANNOTATE }

    /** One shape: filled, or stroked at [width] (filled and stroked when [style] says so). */
    private class Part(
        val path: Path,
        val style: Paint.Style = Paint.Style.FILL,
        val width: Float = 0f,
        val cap: Paint.Cap = Paint.Cap.BUTT,
        val alpha: Int = OPAQUE,
    )

    private val inkParts = ArrayList<Part>()
    private val holes = ArrayList<Part>()
    private val accentParts = ArrayList<Part>()

    private val inkPaint = paintOf(ink)
    private val accentPaint = paintOf(accent)
    private val holePaint = paintOf(0).apply { xfermode = PorterDuffXfermode(PorterDuff.Mode.CLEAR) }

    /** What the icon is composed into, so [setAlpha] and [setColorFilter] reach every part, holes included. */
    private val layer = Paint()

    init {
        when (kind) {
            Kind.KEYBOARD -> {
                val columns = floatArrayOf(8f, 15f, SPACE_COLUMN, 29f, 36f)
                inkParts += Part(shape { card(3f, 11f, 42f, 26f, 6f) })
                holes +=
                    Part(
                        shape {
                            for (x in columns) card(x, 16f, KEY, KEY, 1.2f)
                            for (x in columns) if (x != SPACE_COLUMN) card(x, 23f, KEY, KEY, 1.2f)
                            card(13f, 30f, 22f, 3.4f, 1.7f)
                        },
                    )
                accentParts += Part(shape { card(SPACE_COLUMN, 23f, KEY, KEY, 1.2f) })
            }

            Kind.GAMEPAD -> {
                inkParts += Part(shape { card(2f, 12f, 44f, 24f, 12f) })
                holes +=
                    Part(
                        shape {
                            moveTo(9.5f, 24f)
                            rLineTo(9f, 0f)
                            moveTo(14f, 19.5f)
                            rLineTo(0f, 9f)
                        },
                        Paint.Style.STROKE,
                        3.2f,
                        Paint.Cap.ROUND,
                    )
                holes += Part(shape { addCircle(34f, 24f, 6f, Path.Direction.CW) })
                accentParts += Part(shape { addCircle(34f, 24f, 3f, Path.Direction.CW) })
            }

            Kind.MACROS -> {
                inkParts += Part(shape { card(14f, 5f, 29f, 29f, 6f) }, alpha = BACK_CARD_ALPHA)
                inkParts += Part(shape { card(5f, 14f, 29f, 29f, 6f) })
                accentParts +=
                    Part(
                        shape {
                            moveTo(15.5f, 22.5f)
                            lineTo(26f, 28.5f)
                            lineTo(15.5f, 34.5f)
                            close()
                        },
                        Paint.Style.FILL_AND_STROKE,
                        2.4f,
                    )
            }

            Kind.CAPTURE -> {
                inkParts += Part(shape { addCircle(24f, 24f, 21f, Path.Direction.CW) })
                holes +=
                    Part(
                        shape {
                            moveTo(19.5f, 16.2f)
                            lineTo(45f, 16.2f)
                            moveTo(28.5f, 16.2f)
                            lineTo(41f, 37.9f)
                            moveTo(33f, 24f)
                            lineTo(20f, 46.4f)
                            moveTo(28.5f, 31.8f)
                            lineTo(3f, 31.8f)
                            moveTo(19.5f, 31.8f)
                            lineTo(7f, 10.1f)
                            moveTo(15f, 24f)
                            lineTo(28f, 1.6f)
                        },
                        Paint.Style.STROKE,
                        2f,
                    )
                holes +=
                    Part(
                        shape {
                            moveTo(33f, 24f)
                            lineTo(28.5f, 31.8f)
                            lineTo(19.5f, 31.8f)
                            lineTo(15f, 24f)
                            lineTo(19.5f, 16.2f)
                            lineTo(28.5f, 16.2f)
                            close()
                        },
                    )
                accentParts += Part(shape { addCircle(24f, 24f, 3.6f, Path.Direction.CW) })
            }

            Kind.ANNOTATE -> {
                inkParts +=
                    Part(
                        shape {
                            moveTo(24f, 45f)
                            lineTo(11f, 25f)
                            cubicTo(11f, 17f, 16f, 11f, 17f, 4f)
                            lineTo(31f, 4f)
                            cubicTo(32f, 11f, 37f, 17f, 37f, 25f)
                            close()
                        },
                    )
                holes +=
                    Part(
                        shape {
                            moveTo(24f, 43f)
                            lineTo(24f, 28f)
                        },
                        Paint.Style.STROKE,
                        2f,
                        Paint.Cap.ROUND,
                    )
                accentParts += Part(shape { addCircle(24f, 24f, 3.6f, Path.Direction.CW) })
            }
        }
    }

    override fun draw(canvas: Canvas) {
        val box = bounds
        if (box.isEmpty) return
        val saved = canvas.save()
        canvas.translate(box.left.toFloat(), box.top.toFloat())
        canvas.scale(box.width() / SIZE, box.height() / SIZE)
        // The holes clear only this layer, so the body is cut through and the tile behind it is not.
        canvas.saveLayer(0f, 0f, SIZE, SIZE, layer)
        replay(canvas, inkParts, inkPaint)
        replay(canvas, holes, holePaint)
        replay(canvas, accentParts, accentPaint)
        canvas.restoreToCount(saved)
    }

    private fun replay(
        canvas: Canvas,
        parts: List<Part>,
        paint: Paint,
    ) {
        for (i in parts.indices) {
            val part = parts[i]
            paint.style = part.style
            paint.strokeWidth = part.width
            paint.strokeCap = part.cap
            paint.strokeJoin = Paint.Join.ROUND
            paint.alpha = part.alpha
            canvas.drawPath(part.path, paint)
        }
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

    private fun shape(build: Path.() -> Unit): Path = Path().apply(build)

    private fun Path.card(
        x: Float,
        y: Float,
        width: Float,
        height: Float,
        radius: Float,
    ) = addRoundRect(x, y, x + width, y + height, radius, radius, Path.Direction.CW)

    private fun paintOf(color: Int): Paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = color }

    private companion object {
        const val SIZE = 48f
        const val OPAQUE = 255

        // The macros' back card is the same ink at .35.
        const val BACK_CARD_ALPHA = 89

        // The keyboard keys: 4.5 units square, with the space column the one drawn in the accent.
        const val KEY = 4.5f
        const val SPACE_COLUMN = 22f
    }
}
