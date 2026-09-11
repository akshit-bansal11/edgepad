package me.akshitbansal.edgepad

import android.content.Context
import android.content.res.Configuration
import android.graphics.Typeface

/**
 * The app's four colours, read from resources so night mode picks the dark set (values-night) with no
 * code. Everything is ink on a panel: [line] draws hairlines, [dim] draws secondary text.
 */
class Palette(
    val background: Int,
    val line: Int,
    val ink: Int,
    val dim: Int,
    val dark: Boolean,
) {
    companion object {
        fun of(context: Context): Palette =
            Palette(
                context.getColor(R.color.panel),
                context.getColor(R.color.line),
                context.getColor(R.color.ink),
                context.getColor(R.color.dim),
                dark =
                    (context.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) ==
                        Configuration.UI_MODE_NIGHT_YES,
            )
    }
}

/**
 * The type scale, in sp, with its tracking in em. The design's IBM Plex Mono is stood in for by the
 * system monospace, so no font file ships with the app.
 */
object Type {
    val mono: Typeface = Typeface.MONOSPACE
    val sans: Typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
    val plain: Typeface = Typeface.SANS_SERIF

    /** Ruler labels, section headers, chips. */
    const val MICRO = 11f

    /** Sub-lines under a name, footnotes. */
    const val SMALL = 12f

    /** Button labels. */
    const val LABEL = 12.5f

    /** Explanations under a heading or beside a control. */
    const val CAPTION = 13f
    const val BODY = 14f

    /** Names in a list. */
    const val LEAD = 15f

    /** A dial's number. */
    const val VALUE = 20f
    const val HEADING = 24f
    const val TITLE = 28f
    const val DISPLAY = 34f

    const val TRACKING_WIDE = 0.1f
    const val TRACKING_BUTTON = 0.12f
    const val TRACKING_TIGHT = -0.03f

    /** The small value beside a settings row. */
    const val TRACKING_ROW = 0.1f

    /** Where a line of text's visual centre sits above its baseline, as a fraction of the size. */
    const val CAP_CENTRE = 0.35f
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
    const val ROW = 60f

    /** A full-width button. */
    const val BUTTON = 54f

    /** A page's side margin. */
    const val PAGE = 24f
}
