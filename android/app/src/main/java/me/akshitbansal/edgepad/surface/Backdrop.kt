package me.akshitbansal.edgepad.surface

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.RectF
import android.graphics.Shader
import me.akshitbansal.edgepad.Settings
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.roundToInt
import kotlin.math.sin

/**
 * What the control surface paints behind everything: the theme's panel, a colour, a gradient, or an
 * imported image, then a repeating pattern over it. Everything expensive happens in [resize]; drawing
 * allocates nothing.
 */
class Backdrop(
    settings: Settings,
    private val density: Float,
    private val themeColor: Int,
) {
    private val kind = settings.background
    private val color = settings.backgroundColor
    private val gradientEnd = settings.gradientEnd
    private val angle = settings.gradientAngle
    private val pattern = settings.pattern
    private val cell = settings.patternSize * density
    private val patternPaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = settings.patternColor
            alpha = (settings.patternOpacity * MAX_ALPHA).roundToInt()
            strokeWidth = density
        }
    private val fill = Paint()
    private val image: Bitmap? =
        if (kind == Settings.Background.IMAGE && settings.backgroundImage.exists()) {
            BitmapFactory.decodeFile(settings.backgroundImage.path)
        } else {
            null
        }
    private val source = Rect()
    private val target = RectF()

    fun resize(
        width: Int,
        height: Int,
    ) {
        target.set(0f, 0f, width.toFloat(), height.toFloat())
        fill.shader = null
        when (kind) {
            Settings.Background.GRADIENT -> {
                // The line through the centre at [angle] carries the gradient from colour to end colour.
                val rad = Math.toRadians(angle.toDouble())
                val dx = (cos(rad) * width / 2).toFloat()
                val dy = (sin(rad) * height / 2).toFloat()
                val cx = width / 2f
                val cy = height / 2f
                fill.shader =
                    LinearGradient(cx - dx, cy - dy, cx + dx, cy + dy, color, gradientEnd, Shader.TileMode.CLAMP)
            }

            Settings.Background.IMAGE -> {
                // Centre-crop: the part of the image with the screen's proportions.
                val bitmap = image ?: return
                val scale = maxOf(width.toFloat() / bitmap.width, height.toFloat() / bitmap.height)
                val w = (width / scale).roundToInt()
                val h = (height / scale).roundToInt()
                source.set(
                    (bitmap.width - w) / 2,
                    (bitmap.height - h) / 2,
                    (bitmap.width + w) / 2,
                    (bitmap.height + h) / 2,
                )
            }

            else -> {
                Unit
            }
        }
    }

    fun draw(canvas: Canvas) {
        when (kind) {
            Settings.Background.THEME -> {
                canvas.drawColor(themeColor)
            }

            Settings.Background.COLOR -> {
                canvas.drawColor(color)
            }

            Settings.Background.GRADIENT -> {
                canvas.drawRect(target, fill)
            }

            Settings.Background.IMAGE -> {
                val bitmap = image
                if (bitmap == null) canvas.drawColor(themeColor) else canvas.drawBitmap(bitmap, source, target, null)
            }
        }
        if (pattern == Settings.Pattern.NONE || cell < MIN_CELL_PX) return
        val w = target.width()
        val h = target.height()
        when (pattern) {
            Settings.Pattern.SQUARES -> {
                var x = 0f
                while (x <= w) {
                    canvas.drawLine(x, 0f, x, h, patternPaint)
                    x += cell
                }
                var y = 0f
                while (y <= h) {
                    canvas.drawLine(0f, y, w, y, patternPaint)
                    y += cell
                }
            }

            Settings.Pattern.DOTS -> {
                val r = maxOf(density, cell * DOT)
                var y = 0f
                while (y <= h) {
                    var x = 0f
                    while (x <= w) {
                        canvas.drawCircle(x, y, r, patternPaint)
                        x += cell
                    }
                    y += cell
                }
            }

            Settings.Pattern.CHECKER -> {
                var row = 0
                var y = 0f
                while (y < h) {
                    var col = 0
                    var x = 0f
                    while (x < w) {
                        if ((row + col) % 2 == 0) canvas.drawRect(x, y, x + cell, y + cell, patternPaint)
                        x += cell
                        col++
                    }
                    y += cell
                    row++
                }
            }

            Settings.Pattern.NONE -> {
                Unit
            }
        }
    }

    /** Whether text and controls drawn over this read best in a light colour. */
    val isDark: Boolean
        get() =
            when (kind) {
                Settings.Background.THEME -> luminance(themeColor) < HALF
                Settings.Background.COLOR -> luminance(color) < HALF
                Settings.Background.GRADIENT -> (luminance(color) + luminance(gradientEnd)) / 2 < HALF
                Settings.Background.IMAGE -> true
            }

    private fun luminance(c: Int): Float {
        val r = (c shr 16 and 0xFF) / MAX_ALPHA
        val g = (c shr 8 and 0xFF) / MAX_ALPHA
        val b = (c and 0xFF) / MAX_ALPHA
        return abs(LUMA_R * r + LUMA_G * g + LUMA_B * b)
    }

    private companion object {
        const val MAX_ALPHA = 255f
        const val MIN_CELL_PX = 4f
        const val DOT = 0.06f
        const val HALF = 0.5f
        const val LUMA_R = 0.2126f
        const val LUMA_G = 0.7152f
        const val LUMA_B = 0.0722f
    }
}
