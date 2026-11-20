package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.surface.SurfaceWidget

/**
 * The widgets drawn behind the trackpad: a switch each, then the layout editor that moves and sizes the ones that are
 * on. The AI usage widget is offered only in developer mode. Opened from the surface's settings.
 */
object WidgetsScreen {
    /** Where the rows lead. */
    class Routes(
        val layout: () -> Unit,
        val back: () -> Unit,
    )

    /** The widgets [developer] mode offers: every one, or all but the AI widget. */
    fun shown(developer: Boolean): List<SurfaceWidget> =
        SurfaceWidget.entries.filter { it != SurfaceWidget.AI || developer }

    fun build(
        ui: Ui,
        settings: Settings,
        developer: Boolean,
        routes: Routes,
    ): View =
        // The nav bar draws no title: the large title under it is the screen's name, and says so to a screen reader.
        ui.page(ui.bar("", routes.back, backLabel = ui.string(R.string.settings_surface_title))) {
            largeTitle(ui.string(R.string.widgets_title))
            card {
                shown(developer).forEachIndexed { index, widget ->
                    if (index > 0) hairline()
                    val label = ui.string(widget.nameRes)
                    add(ui.toggle(label, settings.widgetOn(widget)) { on -> settings.setWidgetOn(widget, on) })
                }
            }
            card(Space.XL) { add(ui.link(ui.string(R.string.widgets_layout_title), null, routes.layout)) }
            footnote(ui.string(R.string.widgets_hint))
            // Open-Meteo's licence (CC BY 4.0) asks for this credit wherever its data is shown.
            footnote(ui.string(R.string.wx_credit))
        }
}
