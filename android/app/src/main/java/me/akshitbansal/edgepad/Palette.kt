package me.akshitbansal.edgepad

import android.content.Context
import android.content.res.Configuration
import android.graphics.Paint
import android.graphics.Typeface
import android.text.TextPaint
import android.util.DisplayMetrics
import android.util.TypedValue

/**
 * The app's colours, read from resources so night mode picks the dark set (values-night) with no code.
 * Everything is ink on a panel: [line] draws hairlines, [dim] draws secondary text, [faint] fills a
 * pressed row.
 */
class Palette(
    val background: Int,
    val line: Int,
    val faint: Int,
    val ink: Int,
    val dim: Int,
    val dark: Boolean,
) {
    companion object {
        fun of(context: Context): Palette =
            Palette(
                context.getColor(R.color.panel),
                context.getColor(R.color.line),
                context.getColor(R.color.faint),
                context.getColor(R.color.ink),
                context.getColor(R.color.dim),
                dark =
                    (context.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) ==
                        Configuration.UI_MODE_NIGHT_YES,
            )
    }
}

/** The type scale, in sp, with its tracking in em. One face everywhere: JetBrains Mono, bundled in res/font. */
object Type {
    /** Set once by the activity before any view is built; a resource font needs a Context to load. */
    lateinit var face: Typeface
        private set

    fun load(context: Context) {
        if (!::face.isInitialized) face = context.resources.getFont(R.font.jetbrains_mono)
    }

    /** Section headers, chips, the small value beside a row. */
    const val MICRO = 10f

    /** Sub-lines under a name, footnotes. */
    const val SMALL = 11f

    /** Button labels. */
    const val LABEL = 11f

    /** Explanations under a heading or beside a control. */
    const val CAPTION = 12f
    const val BODY = 13f

    /** Names in a list. */
    const val LEAD = 14f

    /** A screen's name in its title row. */
    const val HEADING = 15f

    /** A page's headline. */
    const val TITLE = 24f

    const val TRACKING_WIDE = 0.12f
    const val TRACKING_BUTTON = 0.14f
    const val TRACKING_TIGHT = -0.01f

    /** The small value beside a settings row. */
    const val TRACKING_ROW = 0.1f

    /** Where a line of text's visual centre sits above its baseline, as a fraction of the size. */
    const val CAP_CENTRE = 0.35f

    /** The label paint drawn on a canvas piece: one face, centred, tracked wide, sized at [MICRO]. */
    fun pieceLabel(metrics: DisplayMetrics): TextPaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = face
            textAlign = Paint.Align.CENTER
            letterSpacing = TRACKING_WIDE
            textSize = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, MICRO, metrics)
        }
}

/** The spacing scale, in dp. */
object Space {
    const val HAIR = 1f
    const val XS = 4f
    const val S = 8f
    const val M = 12f
    const val L = 16f
    const val XL = 24f
    const val XXL = 32f
    const val XXXL = 48f

    /** The smallest touch target anywhere in the app. */
    const val TOUCH = 48f

    /** A settings row. */
    const val ROW = 52f

    /** A full-width button. */
    const val BUTTON = 48f

    /** A screen's title row. */
    const val BAR = 52f

    /** A page's side margin. */
    const val PAGE = 20f
}
