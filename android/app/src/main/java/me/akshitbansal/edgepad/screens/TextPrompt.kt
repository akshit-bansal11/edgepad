package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.content.Context
import android.view.View
import android.widget.EditText
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.TextKind

/**
 * AND-A-9: the phone's own keyboard, dictation and paste, typed on the laptop as one TEXT frame per 255 bytes.
 * Shared by the keyboard screen and the control surface (#65).
 */
fun askText(
    context: Context,
    owner: View,
    send: (Frame) -> Unit,
) {
    val field =
        EditText(context).apply {
            hint = context.getString(R.string.keyboard_type_text)
            importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO
            val pad = (Space.L * resources.displayMetrics.density).toInt()
            setPadding(pad, pad, pad, pad)
        }
    AlertDialog
        .Builder(context)
        .setTitle(R.string.keyboard_type_text)
        .setView(field)
        .setPositiveButton(R.string.keyboard_type_send) { _, _ ->
            TextChunks.split(field.text.toString()).forEach { send(Frame.Text(TextKind.TYPE.id, it)) }
        }.setNegativeButton(android.R.string.cancel, null)
        .showOver(owner)
}
