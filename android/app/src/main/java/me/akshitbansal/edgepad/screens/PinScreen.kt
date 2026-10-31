package me.akshitbansal.edgepad.screens

import android.graphics.drawable.GradientDrawable
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.protocol.Pin

/**
 * The laptop's PIN, asked for during the handshake. Its own keypad on purpose: no input method sees the
 * digits, so there is no suggestion strip, no learned words and no clipboard history to keep them. The digits
 * live in a [PinEntry] for as long as this screen does and are shown only as filled dots.
 *
 * Six digits send the answer at once; the keypad then stays still until the laptop replies, because a new
 * question rebuilds this screen and an accepted one leaves it. [attemptsLeft] below the full five means the
 * last answer was wrong, which the danger line says. [answered] is true when this question was already answered
 * before the activity was rebuilt: the screen then stays waiting, since a second answer would use up an attempt.
 */
class PinScreen(
    private val ui: Ui,
    attemptsLeft: Int,
    answered: Boolean,
    private val onAnswer: (Pin) -> Unit,
    onCancel: () -> Unit,
) {
    private val entry = PinEntry()
    private val dots = List(Pin.LENGTH) { View(ui.context) }
    private var sent = answered
    private lateinit var row: LinearLayout

    val view: View =
        ui.page(centred = true) {
            val sideways = ui.landscape
            val gap = if (sideways) Space.M else Space.XL
            grow()
            val lock =
                ImageView(ui.context).apply {
                    val glyph = ui.context.getDrawable(R.drawable.ic_lock)?.mutate()
                    glyph?.setTint(ui.palette.accent)
                    setImageDrawable(glyph)
                    importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
                }
            add(lock, height = ui.dp(LOCK_DP), width = ui.dp(LOCK_DP))
            headline(ui.string(R.string.pin_title), Type.TITLE, Space.M).apply {
                gravity = Gravity.CENTER
                isAccessibilityHeading = true
            }
            body(ui.string(R.string.pin_body), Space.XS).gravity = Gravity.CENTER
            if (attemptsLeft < ATTEMPTS) {
                val line =
                    ui.context.resources.getQuantityString(R.plurals.pin_attempts_left, attemptsLeft, attemptsLeft)
                secondary(line, color = ui.palette.danger, topDp = Space.S).gravity = Gravity.CENTER
            }
            row =
                LinearLayout(ui.context).apply {
                    gravity = Gravity.CENTER
                    accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_POLITE
                    dots.forEach { dot ->
                        dot.importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
                        addView(
                            dot,
                            LinearLayout.LayoutParams(ui.dp(DOT_DP), ui.dp(DOT_DP)).apply {
                                marginStart = ui.dp(DOT_GAP_DP / 2)
                                marginEnd = ui.dp(DOT_GAP_DP / 2)
                            },
                        )
                    }
                }
            add(row, gap)
            add(keypad(), gap, width = ui.dp(KEYPAD_DP))
            val cancel = ui.button(ui.string(R.string.pin_cancel), Ui.Style.QUIET, onCancel)
            add(cancel, Space.S, width = ui.dp(KEYPAD_DP))
            grow()
        }

    init {
        view.accessibilityPaneTitle = ui.string(R.string.pin_title)
        showDots()
    }

    private fun keypad(): LinearLayout =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            // 1 to 9 in three rows, then an empty cell, 0 and Delete, as on a phone's own PIN pad.
            val rows = listOf(listOf(1, 2, 3), listOf(4, 5, 6), listOf(7, 8, 9), listOf(EMPTY, 0, DELETE))
            rows.forEachIndexed { r, keys ->
                val line = LinearLayout(ui.context)
                keys.forEachIndexed { k, value ->
                    line.addView(
                        key(value),
                        LinearLayout.LayoutParams(0, ui.dp(KEY_DP), 1f).apply {
                            if (k > 0) marginStart = ui.dp(Space.S)
                        },
                    )
                }
                val params = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(KEY_DP))
                if (r > 0) params.topMargin = ui.dp(Space.S)
                addView(line, params)
            }
        }

    private fun key(value: Int): View {
        if (value == EMPTY) return View(ui.context)
        val erases = value == DELETE
        val label = if (erases) ui.string(R.string.pin_delete) else value.toString()
        val size = if (erases) Type.LABEL else Type.TITLE
        return ui.text(label, size, ui.palette.ink, face = Type.bold).apply {
            gravity = Gravity.CENTER
            background = ui.rounded(ui.palette.card, Space.CARD_RADIUS)
            contentDescription = label
            ui.tappable(this, Space.CARD_RADIUS) { if (erases) delete() else press(value) }
        }
    }

    private fun press(digit: Int) {
        if (sent) return
        entry.press(digit)
        showDots()
        if (!entry.isComplete) return
        sent = true
        val pin = entry.pin()
        // The six dots stay filled while the laptop checks; the digits themselves go now.
        entry.clear()
        onAnswer(pin)
    }

    private fun delete() {
        if (sent) return
        entry.delete()
        showDots()
    }

    private fun showDots() {
        // All six once sent, as the screen looked before a rebuild: the entry itself was cleared when they went.
        val filled = if (sent) Pin.LENGTH else entry.length
        dots.forEachIndexed { i, dot ->
            dot.background =
                GradientDrawable().apply {
                    shape = GradientDrawable.OVAL
                    setColor(if (i < filled) ui.palette.accent else ui.palette.off)
                }
        }
        // A count, never the digits, so a screen reader can follow along without saying the PIN aloud.
        row.contentDescription = ui.string(R.string.pin_entered, filled, Pin.LENGTH)
    }

    private companion object {
        /** What the laptop allows before a lock-out; fewer left means the last answer was wrong. */
        const val ATTEMPTS = 5
        const val EMPTY = -1
        const val DELETE = -2
        const val LOCK_DP = 36f
        const val DOT_DP = 14f
        const val DOT_GAP_DP = 14f
        const val KEY_DP = 56f

        /** The keypad is held to this width rather than stretched across a wide screen. */
        const val KEYPAD_DP = 300f
    }
}
