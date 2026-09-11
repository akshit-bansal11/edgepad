package me.akshitbansal.edgepad.surface

import android.content.res.Resources
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Picture
import android.graphics.RectF
import com.caverock.androidsvg.SVG
import com.caverock.androidsvg.SVGParseException
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Type

/**
 * The logo of the app playing, from the SVGs under res/raw, in their own colours. Logos that come in a
 * dark and a light version pick the one that reads on the current theme. An app with no logo gets its
 * initial. Rendered once per app into a Picture and kept.
 */
class AppLogo(
    private val resources: Resources,
    private val dark: Boolean,
) {
    private val pictures = HashMap<Int, Picture?>()
    private val box = RectF()
    private val text =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.mono
            textAlign = Paint.Align.CENTER
        }

    /** True when [app] has a logo of its own. */
    fun known(app: String): Boolean = resource(app) != null

    /** Draws the logo for [app] to fit a [size] box centred on ([cx], [cy]); [color] is only for the initial. */
    fun draw(
        canvas: Canvas,
        app: String,
        cx: Float,
        cy: Float,
        size: Float,
        color: Int,
    ) {
        val picture = resource(app)?.let { id -> pictures.getOrPut(id) { render(id) } }
        if (picture == null) {
            text.color = color
            text.textSize = size * LETTER
            canvas.drawText(app.take(1).uppercase(), cx, cy + text.textSize * Type.CAP_CENTRE, text)
            return
        }
        // Fit the logo's own proportions inside the box.
        val scale = minOf(size / picture.width, size / picture.height)
        val w = picture.width * scale
        val h = picture.height * scale
        box.set(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2)
        canvas.drawPicture(picture, box)
    }

    private fun render(id: Int): Picture? =
        try {
            val svg = SVG.getFromResource(resources, id)
            if (svg.documentViewBox == null) svg.setDocumentViewBox(0f, 0f, svg.documentWidth, svg.documentHeight)
            val viewBox = svg.documentViewBox
            val width = RENDER_PX
            val height = (RENDER_PX * viewBox.height() / viewBox.width()).toInt().coerceAtLeast(1)
            svg.setDocumentWidth("100%")
            svg.setDocumentHeight("100%")
            svg.renderToPicture(width, height)
        } catch (e: SVGParseException) {
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

    /** The light logo on a dark theme, the dark one on a light theme. */
    private fun pick(
        light: Int,
        dark: Int,
    ): Int = if (this.dark) light else dark

    private companion object {
        const val LETTER = 0.5f

        /** Logos are rendered at this width and scaled down; enough for any box the surface draws them in. */
        const val RENDER_PX = 256
    }
}
