package me.akshitbansal.edgepad.screens

import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.Space

/**
 * UX-D15: [count] placeholder rows for a list that is on its way, each a tile and a line of text the size of a real
 * row ([rowDp] tall, [tileDp] square), with the hairlines between rows a real list has. Static shapes: no shimmer,
 * no animator, no timer, so waiting costs the phone nothing. Hidden from a screen reader, which has the words.
 */
internal fun Ui.skeletonRows(
    count: Int,
    rowDp: Float,
    tileDp: Float,
): LinearLayout =
    LinearLayout(context).apply {
        orientation = LinearLayout.VERTICAL
        importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO_HIDE_DESCENDANTS
        repeat(count) { i ->
            if (i > 0) addView(hairline(), ViewGroup.LayoutParams.MATCH_PARENT, dp(Space.HAIR))
            addView(skeletonRow(i, rowDp, tileDp))
        }
    }

private fun Ui.skeletonRow(
    index: Int,
    rowDp: Float,
    tileDp: Float,
): LinearLayout =
    LinearLayout(context).apply {
        gravity = Gravity.CENTER_VERTICAL
        minimumHeight = dp(rowDp)
        val tile = View(context).apply { background = rounded(palette.faint, Space.TILE_RADIUS) }
        addView(tile, LinearLayout.LayoutParams(dp(tileDp), dp(tileDp)).apply { marginEnd = dp(Space.M) })
        // Lines of different lengths, so the rows read as names and not as a pattern.
        val bar = View(context).apply { background = rounded(palette.faint, BAR_RADIUS_DP) }
        addView(bar, LinearLayout.LayoutParams(dp(BAR_DP[index % BAR_DP.size]), dp(BAR_HEIGHT_DP)))
    }

private val BAR_DP = floatArrayOf(150f, 110f, 170f, 130f)
private const val BAR_HEIGHT_DP = 12f
private const val BAR_RADIUS_DP = 6f
