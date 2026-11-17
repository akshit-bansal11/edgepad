package me.akshitbansal.edgepad.screens

import android.os.SystemClock
import android.view.Gravity
import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.Clock

/**
 * A network pairing, in two states. First the code, shown large for the laptop's owner to type into Edgepad
 * there, with a countdown to [deadline] (an [SystemClock.elapsedRealtime] time, so a rotation picks it up where it
 * was) and Cancel. Then, once the laptop has answered ([asking]), the question only the person holding both can
 * answer: did the laptop say the code matched? The laptop's answer alone proves nothing, since a device in the
 * middle could send it, so only Yes keeps the pairing; No, like Cancel and back, ends it with nothing kept.
 *
 * [code] is shown as one box per digit, grouped in two halves like "482 913"; a screen reader hears it digit by digit.
 */
class PairCodeScreen(
    private val ui: Ui,
    code: String,
    laptopName: String,
    private val deadline: Long,
    asking: Boolean,
    onYes: () -> Unit,
    onCancel: () -> Unit,
    onNewCode: () -> Unit,
) {
    private lateinit var countdown: TextView
    private var newCode: View? = null

    private val tick =
        object : Runnable {
            override fun run() {
                val left = ((deadline - SystemClock.elapsedRealtime() + MS_PER_S - 1) / MS_PER_S).coerceAtLeast(0L)
                // ANDU-12: a code at zero says it ran out rather than "Expires in 0 s".
                countdown.text =
                    if (left == 0L) {
                        ui.string(R.string.pair_expired)
                    } else {
                        ui.string(R.string.pair_expires, Clock.remaining(ui.context, left.toInt()))
                    }
                // A view no longer on screen holds the post and never runs it, so the loop ends with the screen.
                // AND-B-15: a code that ran out offers another here instead of leaving the screen.
                if (left > 0L) countdown.postDelayed(this, MS_PER_S) else newCode?.visibility = View.VISIBLE
            }
        }

    private val title =
        if (asking) ui.string(R.string.pair_confirm_title, laptopName) else ui.string(R.string.pair_title)

    val view: View =
        ui.page(centred = true) {
            grow()
            headline(title, Type.TITLE).apply {
                gravity = Gravity.CENTER
                isAccessibilityHeading = true
            }
            val explanation =
                if (asking) ui.string(R.string.pair_confirm_body) else ui.string(R.string.pair_body, laptopName)
            body(explanation, Space.XS).gravity = Gravity.CENTER
            add(digitBoxes(code), Space.XL, width = ui.dp(BUTTON_DP))
            countdown = secondary("", topDp = Space.M).apply { gravity = Gravity.CENTER }
            if (asking) {
                countdown.visibility = View.GONE
                lateinit var yes: TextView
                yes =
                    ui.button(ui.string(R.string.pair_confirm_yes), Ui.Style.FILLED) {
                        // Until the laptop answers, Yes takes no second tap; No stays, to give up.
                        yes.text = ui.string(R.string.pair_confirming)
                        ui.enable(yes, false)
                        onYes()
                    }
                add(yes, Space.XL, width = ui.dp(BUTTON_DP))
                val no = ui.button(ui.string(R.string.pair_confirm_no), Ui.Style.QUIET, onCancel)
                add(no, Space.S, width = ui.dp(BUTTON_DP))
            } else {
                val cancel = ui.button(ui.string(R.string.pin_cancel), Ui.Style.QUIET, onCancel)
                add(cancel, Space.XL, width = ui.dp(BUTTON_DP))
                newCode =
                    ui.button(ui.string(R.string.pair_new_code), Ui.Style.FILLED, onNewCode).also {
                        it.visibility = View.GONE
                        add(it, Space.S, width = ui.dp(BUTTON_DP))
                    }
            }
            grow()
        }

    /**
     * UX-D40: [code] as one box per digit, so each is read and typed on the laptop one at a time. The boxes share the
     * row's width, with a wider gap in the middle where the code is grouped; a screen reader hears one description.
     */
    private fun digitBoxes(code: String): LinearLayout {
        val digits = code.filter(Char::isDigit)
        return LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER
            // Digits read left to right in every language.
            layoutDirection = View.LAYOUT_DIRECTION_LTR
            importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_YES
            contentDescription = ui.string(R.string.pair_code_spoken, digits.toList().joinToString(" "))
            digits.forEachIndexed { i, digit ->
                val box =
                    ui.text(digit.toString(), DIGIT_SP, ui.palette.ink, face = Type.black).apply {
                        gravity = Gravity.CENTER
                        background = ui.rounded(ui.palette.card, Space.CARD_RADIUS)
                        importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
                    }
                val params = LinearLayout.LayoutParams(0, ui.dp(BOX_DP), 1f)
                val groupStart = digits.length >= GROUPED && i == digits.length / 2
                if (i > 0) params.marginStart = ui.dp(if (groupStart) GROUP_GAP_DP else BOX_GAP_DP)
                addView(box, params)
            }
        }
    }

    init {
        view.accessibilityPaneTitle = title
        if (!asking) tick.run()
    }

    private companion object {
        const val DIGIT_SP = 32f
        const val BOX_DP = 60f
        const val BOX_GAP_DP = 6f
        const val GROUP_GAP_DP = 16f

        /** A code of this many digits or more is shown in two groups. */
        const val GROUPED = 6
        const val MS_PER_S = 1_000L

        /** Held to the PIN keypad's width rather than stretched across a wide screen. */
        const val BUTTON_DP = 300f
    }
}
