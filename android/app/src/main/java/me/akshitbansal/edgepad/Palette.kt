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
 * Edgepad 2.0: grouped cards ([card]) on a soft ground ([background]), [ink] text with [dim] secondary text,
 * [line] separators inside a card, [faint] for a pressed row or a neutral fill, and one [accent] for what is
 * selected, on, live or the main action, with [onAccent] drawn on it. [off] is a switch or slider when off.
 */
class Palette(
    val background: Int,
    val card: Int,
    val line: Int,
    val faint: Int,
    val ink: Int,
    val dim: Int,
    val accent: Int,
    val onAccent: Int,
    val accentSoft: Int,
    val off: Int,
    val danger: Int,
    val dark: Boolean,
) {
    companion object {
        fun of(context: Context): Palette =
            Palette(
                context.getColor(R.color.panel),
                context.getColor(R.color.card),
                context.getColor(R.color.line),
                context.getColor(R.color.faint),
                context.getColor(R.color.ink),
                context.getColor(R.color.dim),
                context.getColor(R.color.accent),
                context.getColor(R.color.on_accent),
                context.getColor(R.color.accent_soft),
                context.getColor(R.color.off),
                context.getColor(R.color.danger),
                dark =
                    (context.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) ==
                        Configuration.UI_MODE_NIGHT_YES,
            )
    }
}

/** The type scale, in sp. One family everywhere, Lato (OFL, bundled in res/font), in three weights. */
object Type {
    /** Set once by the activity before any view is built; a resource font needs a Context to load. */
    lateinit var face: Typeface
        private set

    /** Names, titles, buttons and anything chosen. */
    lateinit var bold: Typeface
        private set

    /** Large titles and the numbers a dial shows. */
    lateinit var black: Typeface
        private set

    fun load(context: Context) {
        if (::face.isInitialized) return
        face = context.resources.getFont(R.font.lato_regular)
        bold = context.resources.getFont(R.font.lato_bold)
        black = context.resources.getFont(R.font.lato_black)
    }

    /** A tag or badge, and the small abbreviation over a dial's value. */
    const val MICRO = 12f

    /** Sub-lines under a name, footnotes under a card, section headers, and a segmented control's options. */
    const val SMALL = 13f

    /** Button labels, and a sub-screen's name in its nav bar. */
    const val LABEL = 17f

    /** Row labels, running text and names in a list. */
    const val BODY = 16f

    /** A page's headline. */
    const val TITLE = 24f

    /** A top-level screen's large title. */
    const val LARGE_TITLE = 32f

    /** The value beside a row. */
    const val VALUE = 15f

    const val TRACKING_TIGHT = -0.01f

    /** Line height, as a multiple of the size, for anything that runs to more than one line. */
    const val LEADING = 1.4f

    /** Where a line of text's visual centre sits above its baseline, as a fraction of the size. */
    const val CAP_CENTRE = 0.35f

    /** The label paint drawn on a canvas piece: bold, centred, sized at [SMALL]. */
    fun pieceLabel(metrics: DisplayMetrics): TextPaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = bold
            textAlign = Paint.Align.CENTER
            textSize = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, SMALL, metrics)
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

    /** Between a name and the sub-line under it. */
    const val SUB_GAP = 2f

    /** The smallest touch target anywhere in the app. */
    const val TOUCH = 48f

    /** A settings row. */
    const val ROW = 52f

    /** A full-width button. */
    const val BUTTON = 50f

    /** A screen's nav bar. */
    const val BAR = 48f

    /** A page's side margin. */
    const val PAGE = 16f

    /** Inside a grouped card, either side. */
    const val CARD_PAD = 16f

    /** A grouped card's corners. */
    const val CARD_RADIUS = 14f

    /** An icon tile or a small preview inside a row. */
    const val TILE_RADIUS = 10f

    /** A panel that stands on its own over the ground: an options sheet, a preview, a drawing. */
    const val PANEL_RADIUS = 16f
}
