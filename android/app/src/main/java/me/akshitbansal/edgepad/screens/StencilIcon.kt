package me.akshitbansal.edgepad.screens

import android.graphics.Canvas
import android.graphics.ColorFilter
import android.graphics.Paint
import android.graphics.Path
import android.graphics.PixelFormat
import android.graphics.PorterDuff
import android.graphics.PorterDuffXfermode
import android.graphics.RectF
import android.graphics.drawable.Drawable

/**
 * The Controls tiles' icons, in three colours with true cut-outs: [ink] for the body, [accent] for the one
 * live part, and holes through the body, so whatever the tile is painted shows through them in any state. That
 * rules out a vector drawable, whose colours are fixed in XML, and the palette is only known at run time.
 *
 * The shapes are drawn in a 48-unit square (a 24-unit grid doubled) scaled to the bounds, every one with the same
 * 4-unit margin and about the same weight, and one small accent detail each. They are built once here, because
 * lint rejects allocating while drawing; [draw] only replays them.
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
                inkParts += Part(shape { card(4f, 11f, 40f, 26f, 6f) })
                holes +=
                    Part(
                        shape {
                            for (i in 0 until TOP_KEYS) card(KEY_LEFT + i * KEY_STEP, 16f, KEY_W, KEY_H, 1.4f)
                            for (i in 0 until TOP_KEYS - 1) {
                                card(KEY_LEFT + KEY_STEP / 2 + i * KEY_STEP, 22.4f, KEY_W, KEY_H, 1.4f)
                            }
                        },
                    )
                accentParts += Part(shape { card(14f, 29.2f, 20f, 4f, 2f) })
            }

            Kind.GAMEPAD -> {
                inkParts += Part(shape { card(4f, 13f, 40f, 22f, 11f) })
                holes +=
                    Part(
                        shape {
                            moveTo(10f, 24f)
                            rLineTo(10f, 0f)
                            moveTo(15f, 19f)
                            rLineTo(0f, 10f)
                        },
                        Paint.Style.STROKE,
                        HOLE_STROKE,
                        Paint.Cap.ROUND,
                    )
                accentParts += Part(shape { addCircle(33f, 24f, 4.4f, Path.Direction.CW) })
            }

            Kind.MACROS -> {
                inkParts += Part(shape { card(17f, 5f, 26f, 26f, 6f) }, alpha = BACK_CARD_ALPHA)
                inkParts += Part(shape { card(5f, 17f, 26f, 26f, 6f) })
                accentParts +=
                    Part(
                        shape {
                            moveTo(14.4f, 24.4f)
                            lineTo(24.4f, 30f)
                            lineTo(14.4f, 35.6f)
                            close()
                        },
                    )
            }

            Kind.CAPTURE -> {
                // Lighter than it was: a smaller disc, no inner hexagon, six thin blades.
                inkParts += Part(shape { addCircle(24f, 24f, 19f, Path.Direction.CW) })
                holes +=
                    Part(
                        shape {
                            moveTo(18.4f, 6.4f)
                            rLineTo(8f, 13.8f)
                            moveTo(39.2f, 13.2f)
                            rLineTo(-8f, 13.8f)
                            moveTo(41.2f, 30.8f)
                            rLineTo(-16f, 0f)
                            moveTo(29.6f, 41.6f)
                            rLineTo(-8f, -13.8f)
                            moveTo(8.8f, 34.8f)
                            rLineTo(8f, -13.8f)
                            moveTo(6.8f, 17.2f)
                            rLineTo(16f, 0f)
                        },
                        Paint.Style.STROKE,
                        2.4f,
                    )
                accentParts += Part(shape { addCircle(24f, 24f, 5.2f, Path.Direction.CW) })
            }

            Kind.ANNOTATE -> {
                inkParts +=
                    Part(
                        shape {
                            moveTo(9f, 33.6f)
                            rLineTo(2.6f, -8.8f)
                            rLineTo(18.8f, -18.8f)
                            rLineTo(6.2f, 6.2f)
                            rLineTo(-18.8f, 18.8f)
                            close()
                        },
                    )
                accentParts +=
                    Part(
                        shape {
                            moveTo(25f, 40.4f)
                            cubicTo(30.2f, 37.2f, 34.2f, 42.2f, 41f, 38.4f)
                        },
                        Paint.Style.STROKE,
                        4f,
                        Paint.Cap.ROUND,
                    )
            }

            Kind.CLIPBOARD -> {
                inkParts += Part(shape { card(10f, 9f, 28f, 34f, 6f) })
                holes +=
                    Part(
                        shape {
                            moveTo(17f, 22f)
                            rLineTo(14f, 0f)
                            moveTo(17f, 28.4f)
                            rLineTo(14f, 0f)
                            moveTo(17f, 34.8f)
                            rLineTo(9f, 0f)
                        },
                        Paint.Style.STROKE,
                        3.2f,
                        Paint.Cap.ROUND,
                    )
                accentParts += Part(shape { card(17f, 5f, 14f, 8f, 3f) })
            }

            Kind.PRESENTER -> {
                // A screen on a stand, with the laser's dot on it.
                inkParts += Part(shape { card(5f, 6f, 38f, 25f, 4f) })
                inkParts +=
                    Part(
                        shape {
                            moveTo(24f, 31f)
                            rLineTo(0f, 6f)
                            moveTo(16f, 43f)
                            rLineTo(8f, -6f)
                            rLineTo(8f, 6f)
                        },
                        Paint.Style.STROKE,
                        3.8f,
                        Paint.Cap.ROUND,
                    )
                holes += Part(shape { card(9.4f, 10.4f, 29.2f, 16.2f, 1.6f) })
                accentParts += Part(shape { addCircle(29f, 18.6f, 4.4f, Path.Direction.CW) })
            }

            Kind.SEND -> {
                // A paper plane heading up and right: the near wing in ink, the far one in the accent. Plain
                // fills, no outline, which is what made it heavier than the rest.
                inkParts +=
                    Part(
                        shape {
                            moveTo(5f, 22.4f)
                            lineTo(43f, 6f)
                            lineTo(28f, 43f)
                            lineTo(22f, 27.4f)
                            close()
                        },
                    )
                accentParts +=
                    Part(
                        shape {
                            moveTo(43f, 6f)
                            lineTo(22f, 27.4f)
                            lineTo(28f, 43f)
                            close()
                        },
                    )
            }

            Kind.SYSTEM -> {
                inkParts += Part(shape { card(5f, 7f, 38f, 27f, 6f) })
                inkParts += Part(shape { card(16f, 38f, 16f, 4f, 2f) })
                holes +=
                    Part(
                        shape {
                            moveTo(11f, 21f)
                            rLineTo(6f, 0f)
                            rLineTo(3.6f, -6f)
                            rLineTo(4.8f, 12f)
                            rLineTo(3.6f, -6f)
                            rLineTo(2.4f, 0f)
                        },
                        Paint.Style.STROKE,
                        3.4f,
                        Paint.Cap.ROUND,
                    )
                accentParts += Part(shape { addCircle(36.4f, 21f, 3.2f, Path.Direction.CW) })
            }

            Kind.DEVELOPER -> {
                // A rounded screen with </> cut out of it and one accent slash between the brackets.
                inkParts += Part(shape { card(5f, 8f, 38f, 32f, 6f) })
                holes +=
                    Part(
                        shape {
                            moveTo(18f, 18.4f)
                            rLineTo(-5.6f, 5.6f)
                            rLineTo(5.6f, 5.6f)
                            moveTo(30f, 18.4f)
                            rLineTo(5.6f, 5.6f)
                            rLineTo(-5.6f, 5.6f)
                        },
                        Paint.Style.STROKE,
                        HOLE_STROKE,
                        Paint.Cap.ROUND,
                    )
                accentParts +=
                    Part(
                        shape {
                            moveTo(26.2f, 16.6f)
                            rLineTo(-4.4f, 14.8f)
                        },
                        Paint.Style.STROKE,
                        HOLE_STROKE,
                        Paint.Cap.ROUND,
                    )
            }

            Kind.AI -> {
                inkParts += Part(shape { addCircle(24f, 24f, 21f, Path.Direction.CW) })
                holes += Part(shape { addCircle(24f, 24f, 14f, Path.Direction.CW) })
                accentParts += Part(shape { addCircle(24f, 24f, 5f, Path.Direction.CW) })
            }

            Kind.POWER -> {
                // A disc with the power ring cut out of it (open at the top) and the accent bar through the gap.
                inkParts += Part(shape { addCircle(24f, 24f, 21f, Path.Direction.CW) })
                holes +=
                    Part(
                        shape { arcTo(RectF(15f, 25f, 33f, 43f), 300f, 300f) },
                        Paint.Style.STROKE,
                        HOLE_STROKE,
                        Paint.Cap.ROUND,
                    )
                accentParts +=
                    Part(
                        shape {
                            moveTo(24f, 11f)
                            rLineTo(0f, 14f)
                        },
                        Paint.Style.STROKE,
                        HOLE_STROKE,
                        Paint.Cap.ROUND,
                    )
            }

            Kind.GITHUB -> {
                inkParts +=
                    Part(
                        shape {
                            moveTo(13f, 10f)
                            lineTo(13f, 38f)
                            moveTo(35f, 18f)
                            cubicTo(35f, 29f, 13f, 26f, 13f, 32f)
                        },
                        Paint.Style.STROKE,
                        3.6f,
                        Paint.Cap.ROUND,
                    )
                inkParts += Part(shape { addCircle(13f, 10f, 6f, Path.Direction.CW) })
                inkParts += Part(shape { addCircle(13f, 38f, 6f, Path.Direction.CW) })
                holes += Part(shape { addCircle(13f, 10f, 2.4f, Path.Direction.CW) })
                holes += Part(shape { addCircle(13f, 38f, 2.4f, Path.Direction.CW) })
                accentParts += Part(shape { addCircle(35f, 18f, 6f, Path.Direction.CW) })
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
        const val HOLE_STROKE = 3.8f

        // The macros' back card is the same ink at .35.
        const val BACK_CARD_ALPHA = 89

        // The keyboard keys: 5.2 by 4.4 units, five across the top row and four set half a key in under it.
        const val KEY_W = 5.2f
        const val KEY_H = 4.4f
        const val KEY_STEP = 6.2f
        const val KEY_LEFT = 8.8f
        const val TOP_KEYS = 5
    }
}
