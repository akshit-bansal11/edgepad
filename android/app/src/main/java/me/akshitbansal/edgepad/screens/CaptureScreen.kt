package me.akshitbansal.edgepad.screens

import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.surface.ControlSurface

/**
 * Asking the laptop to take a screenshot or a recording, with the pad under the buttons. The laptop's own
 * tool does the capturing; a region capture waits for the pointer to drag out an area, which is why the
 * trackpad is here at all rather than a bare row of buttons.
 *
 * The pad is the control surface in its trackpad-only mode, so it is the recogniser the main screen uses
 * and a drag is made the same way. The buttons are disabled, with the reason written under them, when the
 * laptop has not named [Feature.CAPTURE]: a button the laptop would silently drop is worse than one that
 * says why it is off.
 */
class CaptureScreen(
    private val ui: Ui,
    settings: Settings,
    state: LaptopState,
    private val send: (Frame) -> Unit,
    onBack: () -> Unit,
) {
    private val available = Feature.CAPTURE in state.features

    val view: View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(ui.palette.background)
            addView(
                ui.bar(ui.string(R.string.controls_capture), onBack, backLabel = ui.string(R.string.back_to_controls)),
            )
            addView(
                buttons(),
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT),
            )
            addView(
                ControlSurface(
                    ui.context,
                    settings,
                    state,
                    send,
                    onOpenSettings = {},
                    onOpenControls = {},
                    trackpadOnly = true,
                ),
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f),
            )
            // Edge-to-edge is enforced: keep the bar and the pad clear of the system bars and the cutout.
            // Leaving the screen, by any route, closes a capture overlay the laptop may still have open. A view hook
            // and not the caller's back handler, so every way out is covered; the laptop presses Escape only if
            // an overlay is pending, so a stray detach costs nothing.
            addOnAttachStateChangeListener(
                object : View.OnAttachStateChangeListener {
                    override fun onViewAttachedToWindow(v: View) = Unit

                    override fun onViewDetachedFromWindow(v: View) {
                        if (available) send(ActionId.CAPTURE_CANCEL.frame())
                    }
                },
            )
            setOnApplyWindowInsetsListener { page, insets ->
                val bars = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
                page.setPadding(bars.left, bars.top, bars.right, bars.bottom)
                insets
            }
        }

    /** The three buttons in a row, and under them either the hint for a region or the reason they are off. */
    private fun buttons(): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            val side = ui.dp(Space.PAGE)
            setPadding(side, 0, side, ui.dp(Space.M))
            val row = LinearLayout(ui.context)
            listOf(
                R.string.capture_region to ActionId.CAPTURE_REGION,
                R.string.capture_screen to ActionId.CAPTURE_SCREEN,
                R.string.capture_record to ActionId.CAPTURE_RECORD,
            ).forEachIndexed { at, (label, action) ->
                val button = ui.button(ui.string(label), Ui.Style.OUTLINED) { send(action.frame()) }
                ui.enable(button, available)
                row.addView(
                    button,
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                        if (at > 0) marginStart = ui.dp(Space.S)
                    },
                )
            }
            addView(row)
            Column(ui, this).secondary(
                ui.string(if (available) R.string.capture_hint else R.string.capture_unavailable),
                topDp = Space.S,
            )
        }
}
