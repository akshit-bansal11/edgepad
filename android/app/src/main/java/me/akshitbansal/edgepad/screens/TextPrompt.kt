package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.content.Context
import android.view.View
import android.view.WindowManager
import android.view.inputmethod.InputMethodManager
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
            // ANDU-13: a half-typed text comes back after a rotation closed the box, which presses neither button.
            setText(TextDraft.text)
            setSelection(text.length)
            onEdited { text ->
                TextDraft.text = text?.toString().orEmpty()
            }
        }
    AlertDialog
        .Builder(context)
        .setTitle(R.string.keyboard_type_text)
        .setView(field)
        .setPositiveButton(R.string.keyboard_type_send) { _, _ ->
            TextDraft.text = ""
            TextChunks.split(field.text.toString()).forEach { send(Frame.Text(TextKind.TYPE.id, it)) }
        }.setNegativeButton(android.R.string.cancel) { _, _ -> TextDraft.text = "" }
        .setOnCancelListener { TextDraft.text = "" }
        .openOver(owner)
        .window
        ?.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_VISIBLE)
    // The box is only there to be typed in, so it opens with the keyboard up (#65). The window flag covers most
    // builds; asking once the field has focus covers the ones that ignore it when the dialog opens in landscape.
    field.requestFocus()
    field.post { context.getSystemService(InputMethodManager::class.java)?.showSoftInput(field, 0) }
}

/** What the Type text box held when it last closed unsent. Process memory only: never stored, never logged. */
private object TextDraft {
    @Volatile var text: String = ""
}
