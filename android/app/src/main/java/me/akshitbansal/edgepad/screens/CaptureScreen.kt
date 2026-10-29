package me.akshitbansal.edgepad.screens

import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.link.CaptureInbox
import me.akshitbansal.edgepad.link.FileInbox
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.CaptureStatus
import me.akshitbansal.edgepad.protocol.CaptureTarget
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.protocol.FileStatus
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
 *
 * "Save to" picks where a capture ends up. Laptop leaves it where the laptop's tool saved it; Phone has the laptop send
 * it here, into the gallery through [CaptureInbox], and the line under the buttons follows it as it arrives.
 */
class CaptureScreen(
    private val ui: Ui,
    private val settings: Settings,
    state: LaptopState,
    private val send: (Frame) -> Unit,
    onBack: () -> Unit,
) {
    private val available = Feature.CAPTURE in state.features

    /** How the last capture went, under the buttons. Empty until something has happened. */
    private val status: TextView = ui.secondary("")

    private val onStep: (FileInbox.Progress) -> Unit = { show(it) }

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
            // Leaving the screen, by any route, closes a capture overlay the laptop may still have open. A view hook
            // and not the caller's back handler, so every way out is covered; the laptop presses Escape only if
            // an overlay is pending, so a stray detach costs nothing.
            addOnAttachStateChangeListener(
                object : View.OnAttachStateChangeListener {
                    override fun onViewAttachedToWindow(v: View) {
                        CaptureInbox.listener = onStep
                    }

                    override fun onViewDetachedFromWindow(v: View) {
                        if (CaptureInbox.listener === onStep) CaptureInbox.listener = null
                        CaptureInbox.cancel()
                        if (available) send(ActionId.CAPTURE_CANCEL.frame())
                    }
                },
            )
            ui.padToBars(this)
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
                val button =
                    ui.button(ui.string(label), Ui.Style.OUTLINED) {
                        // Before the send, so the file this action may bring back always finds its permit.
                        CaptureInbox.permit(action)
                        send(action.frame())
                    }
                ui.enable(button, available)
                row.addView(
                    button,
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                        if (at > 0) marginStart = ui.dp(Space.S)
                    },
                )
            }
            addView(row)
            val column = Column(ui, this)
            column.secondary(
                ui.string(if (available) R.string.capture_hint else R.string.capture_unavailable),
                topDp = Space.S,
            )
            column.add(destination(), topDp = Space.M)
            column.add(status, topDp = Space.S)
            CaptureInbox.last?.let(::show)
        }

    /**
     * "Save to: Laptop | Phone". The choice is kept, handed to [CaptureInbox] so the link can repeat it on every
     * connection, and sent now, since the laptop may never have heard it on this link.
     */
    private fun destination(): View {
        CaptureInbox.install(ui.context.contentResolver, target())
        if (available) send(target().frame())
        return LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_VERTICAL
            addView(
                ui.secondary(ui.string(R.string.capture_save_to)),
                LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f),
            )
            val options: List<CharSequence> =
                listOf(ui.string(R.string.capture_save_laptop), ui.string(R.string.capture_save_phone))
            addView(
                ui.segmented(options, if (settings.captureToPhone) 1 else 0) { chosen ->
                    settings.captureToPhone = chosen == 1
                    CaptureInbox.target = target()
                    send(target().frame())
                },
            )
        }
    }

    private fun target(): CaptureTarget = if (settings.captureToPhone) CaptureTarget.PHONE else CaptureTarget.LAPTOP

    private fun show(step: FileInbox.Progress) {
        status.text =
            when (step) {
                is FileInbox.Progress.Receiving -> ui.string(R.string.capture_receiving, step.name, step.percent)
                is FileInbox.Progress.Saved -> ui.string(R.string.capture_saved, step.folder)
                is FileInbox.Progress.Failed -> ui.string(failure(step.status))
                is FileInbox.Progress.Kept -> ui.string(kept(step.status))
            }
    }

    private fun failure(result: FileStatus): Int =
        when (result) {
            FileStatus.BAD_HASH -> R.string.capture_bad_hash
            FileStatus.WRITE_FAILED -> R.string.capture_write_failed
            else -> R.string.capture_rejected
        }

    private fun kept(reason: CaptureStatus): Int =
        when (reason) {
            CaptureStatus.TOO_LARGE -> R.string.capture_too_large
            CaptureStatus.NOT_FOUND -> R.string.capture_not_found
            CaptureStatus.FAILED -> R.string.capture_send_failed
        }
}
