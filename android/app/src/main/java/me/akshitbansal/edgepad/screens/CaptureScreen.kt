package me.akshitbansal.edgepad.screens

import android.content.res.Configuration
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.CaptureInbox
import me.akshitbansal.edgepad.link.FileInbox
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.CaptureStateToken
import me.akshitbansal.edgepad.protocol.CaptureStatus
import me.akshitbansal.edgepad.protocol.CaptureTarget
import me.akshitbansal.edgepad.protocol.DeviceOs
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.protocol.FileStatus
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.surface.ControlSurface

private const val MATCH = ViewGroup.LayoutParams.MATCH_PARENT
private const val SIDE_DP = 200f
private const val ACTIONS_DP = 100f
private const val TIP_KEY = "capture"

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
 * "To phone" picks where a capture ends up. Off leaves it where the laptop's tool saved it; on has the laptop send
 * it here, into the gallery through [CaptureInbox], and the line over the buttons follows it as it arrives.
 */
class CaptureScreen(
    private val ui: Ui,
    private val settings: Settings,
    private val state: LaptopState,
    private val send: (Frame) -> Unit,
    onBack: () -> Unit,
) {
    private val available = Feature.CAPTURE in state.features

    /** How the last capture went, under the buttons. Empty until something has happened. */
    private val status: TextView =
        ui.secondary("").apply {
            accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_POLITE
            visibility = View.GONE
        }

    private val onStep: (FileInbox.Progress) -> Unit = { show(it) }

    // Built before [view], whose buttons reach it.
    private val pad =
        ControlSurface(
            ui.context,
            settings,
            state,
            send,
            onOpenSettings = {},
            onOpenControls = {},
            trackpadOnly = true,
        )

    // Windows and Mac overlays end a selection on a click; Linux screenshot tools have buttons to press.
    private val swallowTaps = state.os != DeviceOs.LINUX

    // Windows records a dragged region (Win+Shift+R); a Mac opens a toolbar and Linux just starts.
    private val recordDrags = state.os == DeviceOs.WINDOWS

    private val landscape = ui.context.resources.configuration.orientation == Configuration.ORIENTATION_LANDSCAPE

    private val page =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(ui.palette.background)
            addView(
                ui.bar(
                    ui.string(R.string.controls_capture),
                    onBack,
                    destination(),
                    backLabel = ui.string(R.string.back_to_surface),
                ),
            )
            val match = ViewGroup.LayoutParams.MATCH_PARENT
            val actions = actions()
            if (landscape) {
                // The pad takes the width and the actions stand in a column beside it, so the pad keeps its height.
                addView(
                    LinearLayout(ui.context).apply {
                        addView(pad, LinearLayout.LayoutParams(0, match, 1f))
                        addView(actions, LinearLayout.LayoutParams(ui.dp(SIDE_DP), match))
                    },
                    LinearLayout.LayoutParams(match, 0, 1f),
                )
            } else {
                addView(pad, LinearLayout.LayoutParams(match, 0, 1f))
                addView(actions, LinearLayout.LayoutParams(match, ViewGroup.LayoutParams.WRAP_CONTENT))
            }
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

    val view: View =
        FrameLayout(ui.context).apply {
            addView(page, FrameLayout.LayoutParams(MATCH, MATCH))
            if (available) {
                // Clear of the action row under the pad (portrait) or the column beside it (landscape).
                val bottom = if (landscape) Space.L else ACTIONS_DP
                val end = if (landscape) SIDE_DP + Space.L else Space.L
                val kind = StencilIcon.Kind.CAPTURE
                TipCard.show(this, settings, TIP_KEY, kind, R.string.tip_capture, bottom, endDp = end)
            }
        }

    /** The three capture buttons in one row (a column when sideways), and the line saying how the last one went. */
    private fun actions(): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            val side = ui.dp(Space.PAGE)
            setPadding(side, ui.dp(Space.S), side, ui.dp(Space.S))
            if (landscape) gravity = Gravity.CENTER_VERTICAL
            val row = LinearLayout(ui.context).apply { if (landscape) orientation = LinearLayout.VERTICAL }
            listOf(
                R.string.capture_region to ActionId.CAPTURE_REGION,
                R.string.capture_screen to ActionId.CAPTURE_SCREEN,
                R.string.capture_record to ActionId.CAPTURE_RECORD,
            ).forEachIndexed { at, (label, action) ->
                var self: TextView? = null
                val button =
                    ui.button(ui.string(shown(action, label)), Ui.Style.OUTLINED) {
                        // Before the send, so the file this action may bring back always finds its permit.
                        CaptureInbox.permit(action)
                        val startsRecording = action == ActionId.CAPTURE_RECORD && recordDrags && !running
                        pad.selectRegion(action == ActionId.CAPTURE_REGION || startsRecording, swallowTaps)
                        send(action.frame())
                        acknowledge(action, label, self)
                    }
                // Three buttons share one row: tighter sides than a lone button, and a second line if a
                // translation needs it.
                button.setPadding(ui.dp(Space.S), 0, ui.dp(Space.S), 0)
                button.setTextSize(TypedValue.COMPLEX_UNIT_SP, Type.VALUE)
                button.maxLines = 2
                self = button
                ui.enable(button, available)
                val space = ui.dp(Space.S)
                val params =
                    if (landscape) {
                        LinearLayout.LayoutParams(MATCH, ViewGroup.LayoutParams.WRAP_CONTENT)
                    } else {
                        LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                    }
                if (at > 0) {
                    if (landscape) params.topMargin = space else params.marginStart = space
                }
                row.addView(button, params)
            }
            val note = LinearLayout.LayoutParams(MATCH, ViewGroup.LayoutParams.WRAP_CONTENT)
            note.bottomMargin = ui.dp(Space.S)
            addView(status, note)
            addView(row)
            if (!available) say(ui.string(R.string.capture_unavailable))
            CaptureInbox.last?.let(::show)
            if (laptopRecording) {
                say(ui.string(R.string.capture_recording_laptop))
            } else if (running) {
                say(ui.string(R.string.capture_recording))
            }
        }

    /** The line above the buttons. It takes no room while there is nothing to say. */
    private fun say(line: CharSequence) {
        status.text = line
        status.visibility = if (line.isEmpty()) View.GONE else View.VISIBLE
    }

    /**
     * "To phone", a chip in the bar. Off leaves a capture on the laptop. The choice is kept, handed to [CaptureInbox]
     * so the link can repeat it on every
     * connection, and sent now, since the laptop may never have heard it on this link.
     */
    private fun destination(): View {
        CaptureInbox.install(ui.context.contentResolver, target())
        if (available) send(target().frame())
        return ui.toggleChip(ui.string(R.string.capture_to_phone), settings.captureToPhone) { _, on ->
            settings.captureToPhone = on
            CaptureInbox.target = target()
            send(target().frame())
        }
    }

    /**
     * AND-A-10, AND-A-14: say what the tap did. Record is a toggle on the laptop, so its button becomes Stop and a
     * line says the laptop is recording; the others say what was pressed on the laptop.
     */
    private fun acknowledge(
        action: ActionId,
        label: Int,
        button: TextView?,
    ) {
        if (action == ActionId.CAPTURE_RECORD) {
            val recording = CaptureInbox.toggleRecording(CaptureInbox.owner)
            button?.text = ui.string(if (recording) R.string.capture_stop else R.string.capture_record)
            say(if (recording) ui.string(R.string.capture_recording) else "")
        } else {
            say(ui.string(R.string.capture_sent, ui.string(label)))
        }
    }

    /** Whether the laptop this screen sends to is recording, which the inbox remembers across rebuilds. */
    private val running: Boolean
        get() = laptopRecording || CaptureInbox.isRecording(CaptureInbox.owner)

    /** TEXT 23: the laptop says it is recording, which wins over what this app last started. */
    private val laptopRecording: Boolean
        get() = state.captureState == CaptureStateToken.RECORDING

    /** Record reads Stop while a recording runs. */
    private fun shown(
        action: ActionId,
        label: Int,
    ): Int = if (action == ActionId.CAPTURE_RECORD && running) R.string.capture_stop else label

    private fun target(): CaptureTarget = if (settings.captureToPhone) CaptureTarget.PHONE else CaptureTarget.LAPTOP

    private fun show(step: FileInbox.Progress) {
        say(
            when (step) {
                is FileInbox.Progress.Receiving -> ui.string(R.string.capture_receiving, step.name, step.percent)
                is FileInbox.Progress.Saved -> ui.string(R.string.capture_saved, step.folder)
                is FileInbox.Progress.Failed -> ui.string(failure(step.status))
                is FileInbox.Progress.Kept -> ui.string(kept(step.status))
            },
        )
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
