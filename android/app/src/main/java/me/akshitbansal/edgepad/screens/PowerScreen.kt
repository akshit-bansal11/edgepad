package me.akshitbansal.edgepad.screens

import android.view.View
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.PowerState
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.protocol.PowerStateToken

/**
 * What the laptop does about its power when asked from here: lock, sleep and hibernate on a tap, and restart and
 * shut down by starting the laptop's own 60 s countdown. There is no confirmation: the countdown is the undo, and
 * while one runs the page says so and offers Cancel. Each row is drawn only for what the laptop named. The seconds
 * tick on a loop this view owns, started when it is attached and stopped when it is not, so nothing runs while
 * another page is showing. [onRedraw] is handed the function that redraws the page, for the activity to call when the
 * laptop's state or features change, since rebuilding would throw the scroll position back to the top.
 */
object PowerScreen {
    private const val TICK_MS = 1000L

    fun build(
        ui: Ui,
        features: () -> Set<Feature>,
        power: () -> PowerState?,
        laptopName: () -> String,
        send: (ActionId) -> Unit,
        onBack: () -> Unit,
        onRedraw: (redraw: () -> Unit) -> Unit,
    ): View {
        val body = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
        val redraw = {
            body.removeAllViews()
            Column(ui, body).fill(ui, features(), power(), laptopName(), send)
        }
        redraw()
        val page =
            ui.page(ui.bar("", onBack, backLabel = ui.string(R.string.back_to_surface))) {
                largeTitle(ui.string(R.string.controls_power))
                add(body)
            }
        val ticker =
            object : Runnable {
                override fun run() {
                    if (power() != null) redraw()
                    page.postDelayed(this, TICK_MS)
                }
            }
        page.addOnAttachStateChangeListener(
            object : View.OnAttachStateChangeListener {
                override fun onViewAttachedToWindow(v: View) {
                    v.postDelayed(ticker, TICK_MS)
                }

                override fun onViewDetachedFromWindow(v: View) {
                    v.removeCallbacks(ticker)
                }
            },
        )
        onRedraw(redraw)
        return page
    }

    private fun Column.fill(
        ui: Ui,
        features: Set<Feature>,
        power: PowerState?,
        laptopName: String,
        send: (ActionId) -> Unit,
    ) {
        if (power != null) {
            val line =
                if (power.kind == PowerStateToken.RESTART) R.string.power_restarting else R.string.power_shutting_down
            val left = power.secondsLeft(PowerState.now())
            add(ui.secondary(ui.string(line, laptopName, left), Type.BODY, ui.palette.ink))
            add(ui.button(ui.string(R.string.power_cancel), Ui.Style.OUTLINED) { send(ActionId.POWER_CANCEL) }, Space.M)
        }
        card(if (power != null) Space.XL else 0f) {
            action(ui, R.string.power_lock) { send(ActionId.LOCK) }
            if (Feature.SLEEP in features) {
                hairline()
                action(ui, R.string.power_sleep) { send(ActionId.SLEEP) }
            }
            if (Feature.HIBERNATE in features) {
                hairline()
                action(ui, R.string.power_hibernate) { send(ActionId.HIBERNATE) }
            }
        }
        val restart = Feature.RESTART in features
        val shutDown = Feature.SHUT_DOWN in features
        if (restart || shutDown) {
            // A gap, so a tap that means "sleep" cannot land on a row that ends the session.
            card(Space.XL) {
                if (restart) action(ui, R.string.power_restart) { send(ActionId.RESTART) }
                if (restart && shutDown) hairline()
                if (shutDown) action(ui, R.string.power_shut_down) { send(ActionId.SHUT_DOWN) }
            }
        }
    }

    private fun Column.action(
        ui: Ui,
        label: Int,
        onTap: () -> Unit,
    ) {
        val row = ui.row(ui.text(ui.string(label), Type.BODY, ui.palette.ink))
        ui.tappable(row, onTap)
        add(row)
    }
}
