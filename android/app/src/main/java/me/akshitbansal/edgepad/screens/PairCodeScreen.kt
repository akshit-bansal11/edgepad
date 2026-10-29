package me.akshitbansal.edgepad.screens

import android.os.SystemClock
import android.view.Gravity
import android.view.View
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
 * [code] is grouped as "482 913"; a screen reader hears it digit by digit.
 */
class PairCodeScreen(
    private val ui: Ui,
    code: String,
    laptopName: String,
    private val deadline: Long,
    asking: Boolean,
    onYes: () -> Unit,
    onCancel: () -> Unit,
) {
    private lateinit var countdown: TextView

    private val tick =
        object : Runnable {
            override fun run() {
                val left = ((deadline - SystemClock.elapsedRealtime() + MS_PER_S - 1) / MS_PER_S).coerceAtLeast(0L)
                countdown.text = ui.string(R.string.pair_expires, Clock.format(left.toInt()))
                // A view no longer on screen holds the post and never runs it, so the loop ends with the screen.
                if (left > 0L) countdown.postDelayed(this, MS_PER_S)
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
            add(
                ui.text(code, CODE_SP, ui.palette.ink, CODE_TRACKING, Type.black).apply {
                    gravity = Gravity.CENTER
                    val spoken = code.filter(Char::isDigit).toList().joinToString(" ")
                    contentDescription = ui.string(R.string.pair_code_spoken, spoken)
                },
                Space.XL,
            )
            countdown = secondary("", topDp = Space.M).apply { gravity = Gravity.CENTER }
            if (asking) {
                countdown.visibility = View.GONE
                val yes = ui.button(ui.string(R.string.pair_confirm_yes), Ui.Style.FILLED, onYes)
                add(yes, Space.XL, width = ui.dp(BUTTON_DP))
                val no = ui.button(ui.string(R.string.pair_confirm_no), Ui.Style.QUIET, onCancel)
                add(no, Space.S, width = ui.dp(BUTTON_DP))
            } else {
                val cancel = ui.button(ui.string(R.string.pin_cancel), Ui.Style.QUIET, onCancel)
                add(cancel, Space.XL, width = ui.dp(BUTTON_DP))
            }
            grow()
        }

    init {
        view.accessibilityPaneTitle = title
        if (!asking) tick.run()
    }

    private companion object {
        const val CODE_SP = 56f
        const val CODE_TRACKING = 0.08f
        const val MS_PER_S = 1_000L

        /** Held to the PIN keypad's width rather than stretched across a wide screen. */
        const val BUTTON_DP = 300f
    }
}
