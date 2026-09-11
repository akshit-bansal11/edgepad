package me.akshitbansal.edgepad.screens

import android.content.Context
import android.view.View
import android.widget.Button
import me.akshitbansal.edgepad.R

/** The link dropped on its own; the app is trying the remembered laptop again. */
object ReconnectingScreen {
    fun build(
        context: Context,
        laptop: String,
        onCancel: () -> Unit,
    ): View =
        Page.build(context) {
            title(R.string.reconnecting_title)
            body(context.getString(R.string.reconnecting_body, laptop))
            gap()
            addView(
                Button(context).apply {
                    setText(R.string.reconnecting_cancel)
                    setOnClickListener { onCancel() }
                },
            )
        }
}
