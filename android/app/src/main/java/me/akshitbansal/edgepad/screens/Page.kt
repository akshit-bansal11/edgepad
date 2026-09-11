package me.akshitbansal.edgepad.screens

import android.content.Context
import android.util.TypedValue
import android.view.View
import android.view.WindowInsets
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView

private const val PADDING_DP = 20f
private const val TITLE_SP = 24f
private const val BODY_SP = 15f
private const val HEADING_SP = 17f
private const val GAP_DP = 12f

/** The scaffolding every plain screen shares: a scrolling column that keeps clear of the system bars. */
object Page {
    fun build(
        context: Context,
        fill: LinearLayout.() -> Unit,
    ): View {
        val pad = context.dp(PADDING_DP)
        val column =
            LinearLayout(context).apply {
                orientation = LinearLayout.VERTICAL
                fill()
            }
        return ScrollView(context).apply {
            addView(column)
            // Edge-to-edge is enforced from targetSdk 35: keep content clear of the system bars.
            setOnApplyWindowInsetsListener { view, insets ->
                val bars = insets.getInsets(WindowInsets.Type.systemBars())
                view.setPadding(pad + bars.left, pad + bars.top, pad + bars.right, pad + bars.bottom)
                insets
            }
        }
    }
}

fun LinearLayout.title(resId: Int): TextView = label(context.getString(resId), TITLE_SP)

fun LinearLayout.heading(resId: Int): TextView = label(context.getString(resId), HEADING_SP)

fun LinearLayout.body(resId: Int): TextView = label(context.getString(resId), BODY_SP)

fun LinearLayout.body(text: CharSequence): TextView = label(text, BODY_SP)

fun LinearLayout.gap() {
    addView(View(context), LinearLayout.LayoutParams(0, context.dp(GAP_DP)))
}

private fun LinearLayout.label(
    text: CharSequence,
    sp: Float,
): TextView =
    TextView(context)
        .apply {
            this.text = text
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp)
            setPadding(0, 0, 0, context.dp(GAP_DP))
        }.also { addView(it) }

private fun Context.dp(value: Float): Int = (value * resources.displayMetrics.density).toInt()
