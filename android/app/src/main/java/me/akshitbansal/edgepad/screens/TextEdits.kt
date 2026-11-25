package me.akshitbansal.edgepad.screens

import android.text.Editable
import android.text.TextWatcher
import android.widget.TextView

/**
 * Runs [action] with the field's text after every edit. The app has no androidx core-ktx, whose
 * `doAfterTextChanged` this stands in for, and every field here only cares about the text once it has changed.
 */
fun TextView.onEdited(action: (Editable?) -> Unit) {
    addTextChangedListener(
        object : TextWatcher {
            override fun beforeTextChanged(
                text: CharSequence?,
                start: Int,
                count: Int,
                after: Int,
            ) = Unit

            override fun onTextChanged(
                text: CharSequence?,
                start: Int,
                before: Int,
                count: Int,
            ) = Unit

            override fun afterTextChanged(text: Editable?) = action(text)
        },
    )
}
