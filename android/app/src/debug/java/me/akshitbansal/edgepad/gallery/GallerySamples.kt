package me.akshitbansal.edgepad.gallery

import android.content.Context
import android.view.View
import android.widget.FrameLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.gamepad.GamepadStore
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.protocol.DeviceOs
import me.akshitbansal.edgepad.screens.ControlsPopup
import me.akshitbansal.edgepad.screens.FnMode
import me.akshitbansal.edgepad.screens.GamepadLayoutScreen
import me.akshitbansal.edgepad.screens.KeyboardScreen
import me.akshitbansal.edgepad.screens.KeyboardSize
import me.akshitbansal.edgepad.screens.MediaLayoutScreen
import me.akshitbansal.edgepad.screens.Ui
import me.akshitbansal.edgepad.surface.ControlSurface

/** The sample state behind each gallery screen name. A new screen adds one case to [build]. */
class GallerySamples(
    private val context: Context,
) {
    private val ui = Ui(context)
    private val settings = Settings(context)

    /** The screen called [name], or null when there is none. */
    fun build(name: String): View? =
        when (name) {
            "controls" -> blank()
            "keyboard" ->
                KeyboardScreen.build(
                    ui,
                    settings.keyTextScale,
                    KeyboardSize.FULL,
                    FnMode.WITH_FN,
                    DeviceOs.WINDOWS,
                    onKey = { _, _ -> },
                    onAction = {},
                    onSettings = {},
                    send = {},
                    onBack = {},
                )
            "gamepad-layout" -> GamepadLayoutScreen.build(ui, GamepadStore(context)) {}
            "media-layout" -> MediaLayoutScreen.build(ui, settings) {}
            "surface" -> ControlSurface(context, settings, LaptopState(), {}, {}, {})
            else -> null
        }

    /** An empty page in the app's background: what the Controls popup opens over, and the answer to an unknown name. */
    fun blank(): View = FrameLayout(context).apply { setBackgroundColor(ui.palette.background) }

    /** Opens the layout editor's options panel by tapping its options button inside [root]. */
    fun openOptions(root: View) {
        val found = ArrayList<View>()
        val label = context.getString(R.string.editor_options)
        root.findViewsWithText(found, label, View.FIND_VIEWS_WITH_CONTENT_DESCRIPTION)
        found.firstOrNull()?.performClick()
    }

    /** Opens the Controls popup over [owner], every tile available. */
    fun showControls(owner: View) {
        ControlsPopup(ui, settings) {}.show(owner, capture = true, annotate = true)
    }
}
