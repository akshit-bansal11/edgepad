package me.akshitbansal.edgepad.surface

import android.content.res.Resources
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.ImageDecoder
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.RectF
import android.graphics.Shader
import android.util.Log
import me.akshitbansal.edgepad.Settings
import java.io.File
import java.io.IOException
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.roundToInt
import kotlin.math.sin

private const val CHANNEL_MAX = 255f
private const val HALF = 0.5f
private const val LUMA_R = 0.2126f
private const val LUMA_G = 0.7152f
private const val LUMA_B = 0.0722f

/** The side of the thumbnail an image's brightness is averaged over: enough to tell dark from light. */
private const val LUMA_SAMPLE = 16

/**
 * What the control surface paints behind everything: the theme's background, a colour, a gradient, or an
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
            alpha = (settings.patternOpacity * CHANNEL_MAX).roundToInt()
            strokeWidth = density
        }
    private val fill = Paint()
    private val picture =
        if (kind == Settings.Background.IMAGE) BackgroundImage.load(settings.backgroundImage) else null
    private val image: Bitmap? = picture?.bitmap
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

    /** Whether this is the theme's own background, which the theme's own ink and accent are made for. */
    val themed: Boolean get() = kind == Settings.Background.THEME

    /** Whether text and controls drawn over this read best in a light colour. */
    val isDark: Boolean
        get() =
            when (kind) {
                Settings.Background.THEME -> luminance(themeColor) < HALF

                Settings.Background.COLOR -> luminance(color) < HALF

                Settings.Background.GRADIENT -> (luminance(color) + luminance(gradientEnd)) / 2 < HALF

                // Measured once, when the image was decoded. It was taken to be dark until 3.2, which drew
                // light controls on a photo of a snowfield. An image that is missing or failed to decode is
                // drawn as the theme's background, so it reads as that.
                Settings.Background.IMAGE -> picture?.dark ?: (luminance(themeColor) < HALF)
            }

    private companion object {
        const val MIN_CELL_PX = 4f
        const val DOT = 0.06f
    }
}

/**
 * The imported background image, decoded once and kept. The control surface and the Appearance preview each
 * build a [Backdrop], the preview on every change to any setting, and until 3.2 each of those decoded the
 * whole file at full resolution on the UI thread: a phone camera's photo, tens of megabytes of bitmap, again
 * and again while a slider moved.
 *
 * Now it is decoded down to about the screen's size, turned the way the photo's EXIF says (ImageDecoder does
 * that, where BitmapFactory ignored it and drew a portrait photo on its side), measured for brightness once,
 * and cached against the file's size and modification time, so a new import is noticed and nothing else is.
 * The activity loads it on the thread that copies an import in, so the UI thread finds it already decoded.
 */
object BackgroundImage {
    class Decoded(
        val bitmap: Bitmap,
        /** Whether the image is mostly dark, so what is drawn over it should be light. */
        val dark: Boolean,
    )

    private var key = ""
    private var cached: Decoded? = null

    /** The image in [file], or null when there is none or it is not a picture this phone can decode. */
    @Synchronized
    fun load(file: File): Decoded? {
        if (!file.isFile) return null
        // Not the path: an import decodes its copy before renaming it into place, and a rename keeps the size
        // and the time, so the picture decoded under the copy's name is found again under the real one.
        val key = "${file.length()}:${file.lastModified()}"
        if (key != this.key) {
            this.key = key
            cached = decode(file)
        }
        return cached
    }

    private fun decode(file: File): Decoded? =
        try {
            val screen = Resources.getSystem().displayMetrics
            val target = maxOf(screen.widthPixels, screen.heightPixels)
            val bitmap =
                ImageDecoder.decodeBitmap(ImageDecoder.createSource(file)) { decoder, info, _ ->
                    // Software, because the brightness is read back pixel by pixel, which a hardware bitmap
                    // refuses; the size is what keeps that affordable.
                    decoder.allocator = ImageDecoder.ALLOCATOR_SOFTWARE
                    decoder.setTargetSampleSize(sampleSize(maxOf(info.size.width, info.size.height), target))
                }
            Decoded(bitmap, averageLuminance(bitmap) < HALF)
        } catch (e: IOException) {
            // Not a picture after all, or one this phone has no decoder for: the backdrop falls back to the theme.
            Log.w("Edgepad", "The background image could not be decoded", e)
            null
        }

    private fun averageLuminance(bitmap: Bitmap): Float {
        val small = Bitmap.createScaledBitmap(bitmap, LUMA_SAMPLE, LUMA_SAMPLE, true)
        val pixels = IntArray(LUMA_SAMPLE * LUMA_SAMPLE)
        small.getPixels(pixels, 0, LUMA_SAMPLE, 0, 0, LUMA_SAMPLE, LUMA_SAMPLE)
        if (small !== bitmap) small.recycle()
        return pixels.sumOf { luminance(it).toDouble() }.toFloat() / pixels.size
    }
}

/**
 * The largest power of two to divide an image's longest side by that still leaves it at least [target]
 * pixels, so a photo is decoded no smaller than the screen and no larger than twice it. A power of two
 * because that is what a decoder can skip pixels by cheaply.
 */
internal fun sampleSize(
    longest: Int,
    target: Int,
): Int {
    var sample = 1
    while (target > 0 && longest / (sample * 2) >= target) sample *= 2
    return sample
}

private fun luminance(c: Int): Float {
    val r = (c shr 16 and 0xFF) / CHANNEL_MAX
    val g = (c shr 8 and 0xFF) / CHANNEL_MAX
    val b = (c and 0xFF) / CHANNEL_MAX
    return abs(LUMA_R * r + LUMA_G * g + LUMA_B * b)
}
