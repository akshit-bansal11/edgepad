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
    enum class Kind { KEYBOARD, GAMEPAD, MACROS, CAPTURE, ANNOTATE, CLIPBOARD, PRESENTER, SYSTEM, AI, GITHUB, SEND }

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
                inkParts += Part(shape { card(2f, 11f, 44f, 26f, 6f) })
                holes +=
                    Part(
                        shape {
                            for (i in 0 until TOP_KEYS) card(KEY_LEFT + i * KEY_STEP, 15.5f, KEY, KEY, 1.2f)
                            for (i in 0 until TOP_KEYS - 1) {
                                card(KEY_LEFT + KEY_STEP / 2 + i * KEY_STEP, 22f, KEY, KEY, 1.2f)
                            }
                        },
                    )
                accentParts += Part(shape { card(13f, 28.5f, 22f, 4f, 2f) })
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
                // A pencil laid on the diagonal, its point at the lower left, over the accent stroke it drew.
                inkParts +=
                    Part(
                        shape {
                            moveTo(34.5f, 6.5f)
                            lineTo(41.5f, 13.5f)
                            lineTo(17.5f, 37.5f)
                            lineTo(10.5f, 30.5f)
                            close()
                            moveTo(10.5f, 30.5f)
                            lineTo(17.5f, 37.5f)
                            lineTo(6f, 42f)
                            close()
                        },
                    )
                holes +=
                    Part(
                        shape {
                            moveTo(12f, 29f)
                            lineTo(19f, 36f)
                            moveTo(29f, 12f)
                            lineTo(36f, 19f)
                        },
                        Paint.Style.STROKE,
                        1.8f,
                    )
                accentParts +=
                    Part(
                        shape {
                            moveTo(22f, 43f)
                            cubicTo(28f, 38f, 33f, 46f, 43f, 40f)
                        },
                        Paint.Style.STROKE,
                        3.2f,
                        Paint.Cap.ROUND,
                    )
            }

            Kind.CLIPBOARD -> {
                inkParts += Part(shape { card(8f, 6f, 32f, 39f, 5f) })
                holes +=
                    Part(
                        shape {
                            moveTo(15f, 21f)
                            lineTo(33f, 21f)
                            moveTo(15f, 28f)
                            lineTo(33f, 28f)
                            moveTo(15f, 35f)
                            lineTo(26f, 35f)
                        },
                        Paint.Style.STROKE,
                        3f,
                        Paint.Cap.ROUND,
                    )
                accentParts += Part(shape { card(16f, 2f, 16f, 9f, 3f) })
            }

            Kind.PRESENTER -> {
                // A screen on a stand, with the laser's dot and its glow on it.
                inkParts += Part(shape { card(5f, 4f, 38f, 26f, 3f) })
                inkParts +=
                    Part(
                        shape {
                            moveTo(24f, 30f)
                            lineTo(24f, 39f)
                            moveTo(24f, 39f)
                            lineTo(16f, 45f)
                            moveTo(24f, 39f)
                            lineTo(32f, 45f)
                        },
                        Paint.Style.STROKE,
                        3f,
                        Paint.Cap.ROUND,
                    )
                holes += Part(shape { card(9f, 8f, 30f, 18f, 1.5f) })
                accentParts += Part(shape { addCircle(30f, 17f, 6f, Path.Direction.CW) }, alpha = BACK_CARD_ALPHA)
                accentParts += Part(shape { addCircle(30f, 17f, 3f, Path.Direction.CW) })
            }

            Kind.SEND -> {
                // A paper plane heading up and right: the near wing in ink, the far one in the accent.
                inkParts +=
                    Part(
                        shape {
                            moveTo(44f, 5f)
                            lineTo(4f, 22f)
                            lineTo(19f, 29f)
                            close()
                        },
                        Paint.Style.FILL_AND_STROKE,
                        2f,
                    )
                accentParts +=
                    Part(
                        shape {
                            moveTo(44f, 5f)
                            lineTo(22f, 31f)
                            lineTo(28f, 43f)
                            close()
                        },
                        Paint.Style.FILL_AND_STROKE,
                        2f,
                    )
                inkParts +=
                    Part(
                        shape {
                            moveTo(19f, 29f)
                            lineTo(19f, 41f)
                            lineTo(25f, 35f)
                            close()
                        },
                        Paint.Style.FILL_AND_STROKE,
                        2f,
                    )
            }

            Kind.SYSTEM -> {
                inkParts += Part(shape { card(3f, 6f, 42f, 28f, 5f) })
                inkParts += Part(shape { card(16f, 38f, 16f, 4f, 2f) })
                holes +=
                    Part(
                        shape {
                            moveTo(9f, 21f)
                            lineTo(16f, 21f)
                            lineTo(20f, 12f)
                            lineTo(26f, 27f)
                            lineTo(29f, 21f)
                        },
                        Paint.Style.STROKE,
                        2.6f,
                        Paint.Cap.ROUND,
                    )
                accentParts += Part(shape { addCircle(36f, 21f, 3.6f, Path.Direction.CW) })
            }

            Kind.AI -> {
                inkParts += Part(shape { addCircle(24f, 24f, 21f, Path.Direction.CW) })
                holes += Part(shape { addCircle(24f, 24f, 14f, Path.Direction.CW) })
                accentParts += Part(shape { addCircle(24f, 24f, 5f, Path.Direction.CW) })
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

        // The macros' back card is the same ink at .35.
        const val BACK_CARD_ALPHA = 89

        // The keyboard keys: 4.6 units square, six across the top row and five set half a key in under it.
        const val KEY = 4.6f
        const val KEY_STEP = 6.2f
        const val KEY_LEFT = 6.5f
        const val TOP_KEYS = 6
    }
}
