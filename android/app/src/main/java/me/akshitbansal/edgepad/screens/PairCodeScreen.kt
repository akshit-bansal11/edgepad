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
 * A network pairing's code, shown large for the laptop's owner to type into Edgepad there. Nothing is typed
 * here: the link carries on by itself once the laptop confirms, and Cancel ends the attempt. [code] is grouped
 * as "482 913"; a screen reader hears it digit by digit. The countdown runs to [deadline], an
 * [SystemClock.elapsedRealtime] time, so a rotation picks it up where it was.
 */
class PairCodeScreen(
    private val ui: Ui,
    code: String,
    laptopName: String,
    private val deadline: Long,
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

    val view: View =
        ui.page(centred = true) {
            grow()
            headline(ui.string(R.string.pair_title), Type.TITLE).apply {
                gravity = Gravity.CENTER
                isAccessibilityHeading = true
            }
            body(ui.string(R.string.pair_body, laptopName), Space.XS).gravity = Gravity.CENTER
            add(
                ui.text(code, CODE_SP, ui.palette.ink, CODE_TRACKING, Type.black).apply {
                    gravity = Gravity.CENTER
                    val spoken = code.filter { it.isDigit() }.toList().joinToString(" ")
                    contentDescription = ui.string(R.string.pair_code_spoken, spoken)
                },
                Space.XL,
            )
            countdown = secondary("", topDp = Space.M).apply { gravity = Gravity.CENTER }
            add(ui.button(ui.string(R.string.pin_cancel), Ui.Style.QUIET, onCancel), Space.XL, width = ui.dp(BUTTON_DP))
            grow()
        }

    init {
        view.accessibilityPaneTitle = ui.string(R.string.pair_title)
        tick.run()
    }

    private companion object {
        const val CODE_SP = 56f
        const val CODE_TRACKING = 0.08f
        const val MS_PER_S = 1_000L

        /** Held to the PIN keypad's width rather than stretched across a wide screen. */
        const val BUTTON_DP = 300f
    }
}
