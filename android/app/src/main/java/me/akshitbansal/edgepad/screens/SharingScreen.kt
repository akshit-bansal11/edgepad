package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space

/**
 * The one short question after the first connection: whether to share the clipboard with laptops, and whether to
 * keep them connected while Edgepad is minimized. Both start off, and nothing is shared or kept until the person
 * says so. Each switch writes its setting as it is moved, so that [onChanged] can bring the links in line with it
 * at once; [onContinue] ends the question for good and goes on to the controls.
 */
object SharingScreen {
    fun build(
        ui: Ui,
        settings: Settings,
        onChanged: () -> Unit,
        onContinue: () -> Unit,
    ): View =
        ui.page(footer = {
            add(ui.button(ui.string(R.string.sharing_continue), Ui.Style.FILLED, onContinue))
        }) {
            largeTitle(ui.string(R.string.sharing_title))
            body(ui.string(R.string.sharing_body), Space.XS)
            card(Space.XL) {
                add(
                    ui.toggle(ui.string(R.string.clipboard_share), settings.clipboardSync) {
                        settings.clipboardSync = it
                        onChanged()
                    },
                )
            }
            footnote(ui.string(R.string.clipboard_share_caption))
            card(Space.XL) {
                add(
                    ui.toggle(ui.string(R.string.stay_connected), settings.stayConnected) {
                        settings.stayConnected = it
                        onChanged()
                    },
                )
            }
            footnote(ui.string(R.string.stay_connected_caption))
        }
}
