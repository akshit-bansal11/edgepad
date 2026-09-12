package me.akshitbansal.edgepad.surface

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.drawable.Drawable
import com.caverock.androidsvg.SVG
import com.caverock.androidsvg.SVGParseException
import me.akshitbansal.edgepad.R

/**
 * The logo of the app playing, from the SVGs under res/raw, in their own colours. Logos that come in a
 * dark and a light version pick the one made for the current theme. An app with no logo gets a
 * music note (Lucide). Rendered once per app into a bitmap and kept: a recorded Picture drops gradient stops with
 * transparency (Netflix), a bitmap keeps everything the renderer can draw.
 */
class AppLogo(
    context: Context,
    private val dark: Boolean,
) {
    private val resources = context.resources
    private val bitmaps = HashMap<Int, Bitmap?>()
    private val box = RectF()
    private val smooth = Paint(Paint.FILTER_BITMAP_FLAG or Paint.ANTI_ALIAS_FLAG)
    private val note: Drawable = checkNotNull(context.getDrawable(R.drawable.ic_music)).mutate()

    /** Draws the logo for [app] to fit a [size] box centred on ([cx], [cy]); [color] is only for the note. */
    fun draw(
        canvas: Canvas,
        app: String,
        cx: Float,
        cy: Float,
        size: Float,
        color: Int,
    ) {
        val bitmap = resource(app)?.let { id -> bitmaps.getOrPut(id) { render(id) } }
        if (bitmap == null) {
            note.setTint(color)
            val half = (size * NOTE / 2).toInt()
            note.setBounds(cx.toInt() - half, cy.toInt() - half, cx.toInt() + half, cy.toInt() + half)
            note.draw(canvas)
            return
        }
        // Fit the logo's own proportions inside the box.
        val scale = minOf(size / bitmap.width, size / bitmap.height)
        val w = bitmap.width * scale
        val h = bitmap.height * scale
        box.set(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2)
        canvas.drawBitmap(bitmap, null, box, smooth)
    }

    private fun render(id: Int): Bitmap? =
        try {
            val svg = SVG.getFromResource(resources, id)
            if (svg.documentViewBox == null) svg.setDocumentViewBox(0f, 0f, svg.documentWidth, svg.documentHeight)
            val viewBox = svg.documentViewBox
            val width = RENDER_PX
            val height = (RENDER_PX * viewBox.height() / viewBox.width()).toInt().coerceAtLeast(1)
            svg.setDocumentWidth("100%")
            svg.setDocumentHeight("100%")
            val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
            svg.renderToCanvas(Canvas(bitmap))
            bitmap
        } catch (e: SVGParseException) {
            null
        } catch (e: IllegalArgumentException) {
            null
        }

    private fun resource(app: String): Int? {
        val name = app.trim().lowercase()
        return when {
            name.isEmpty() -> {
                null
            }

            "spotify" in name -> {
                R.raw.logo_spotify
            }

            "youtube music" in name -> {
                R.raw.logo_youtube_music
            }

            "youtube" in name -> {
                R.raw.logo_youtube
            }

            "netflix" in name -> {
                R.raw.logo_netflix
            }

            "prime" in name -> {
                pick(R.raw.logo_prime_video_light, R.raw.logo_prime_video_dark)
            }

            "apple music" in name || "itunes" in name -> {
                R.raw.logo_apple_music
            }

            "apple tv+" in name || "apple tv plus" in name -> {
                pick(
                    R.raw.logo_apple_tv_plus_light,
                    R.raw.logo_apple_tv_plus_dark,
                )
            }

            "apple tv" in name || "tv.apple" in name -> {
                pick(R.raw.logo_apple_tv_light, R.raw.logo_apple_tv_dark)
            }

            "disney" in name -> {
                R.raw.logo_disney_plus
            }

            "hbo" in name || "max" == name -> {
                R.raw.logo_hbo_max
            }

            "hulu" in name -> {
                R.raw.logo_hulu
            }

            "crunchyroll" in name -> {
                R.raw.logo_crunchyroll
            }

            "paramount" in name -> {
                R.raw.logo_paramount
            }

            "peacock" in name -> {
                pick(R.raw.logo_peacock_light, R.raw.logo_peacock_dark)
            }

            "sky" in name -> {
                R.raw.logo_sky
            }

            "facebook" in name -> {
                R.raw.logo_facebook
            }

            "instagram" in name -> {
                R.raw.logo_instagram
            }

            "linkedin" in name -> {
                R.raw.logo_linkedin
            }

            "twitter" in name || name == "x" -> {
                R.raw.logo_twitter
            }

            else -> {
                null
            }
        }
    }

    /** The files are named for the theme they are drawn on: the dark one on a dark theme, the light one on a light theme. */
    private fun pick(
        light: Int,
        dark: Int,
    ): Int = if (this.dark) dark else light

    private companion object {
        const val NOTE = 0.8f

        /** Logos are rendered at this width and scaled down; enough for any box the surface draws them in. */
        const val RENDER_PX = 256
    }
}
