package me.akshitbansal.edgepad.gallery

import android.app.Activity
import android.content.pm.ActivityInfo
import android.os.Bundle
import android.view.View
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.screens.hideBars

/**
 * Debug-only. Renders one named screen with sample state so CI can screenshot it:
 * `am start -n me.akshitbansal.edgepad/.gallery.GalleryActivity --es screen controls --ez land true`.
 * Day or night is the system's (`cmd uimode night yes|no`, set by scripts/gallery.sh): the intent is not yet
 * attached when attachBaseContext runs, so an override read from it there always saw the default, night.
 */
class GalleryActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        requestedOrientation =
            if (intentFlag(LAND)) {
                ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
            } else {
                ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
            }
        window.hideBars()
        // The app's fonts, as MainActivity loads them; every screen reads them.
        Type.load(this)
        val samples = GallerySamples(this)
        val screen = intent.getStringExtra(SCREEN).orEmpty()
        val view: View = samples.build(screen) ?: samples.blank()
        setContentView(view)
        if (screen == "controls") view.post { samples.showControls(view) }
        if (screen == "presenter-sheet") view.post { samples.showLaserSheet(view) }
        if (screen == "settings-capture") view.post { samples.showCaptureSection(view) }
        if (screen == "media-layout" ||
            screen == "widget-layout" ||
            screen == "gamepad-layout"
        ) {
            view.post { samples.openOptions(view) }
        }
    }

    private fun intentFlag(name: String): Boolean = intent?.getBooleanExtra(name, true) ?: true

    private companion object {
        const val SCREEN = "screen"
        const val LAND = "land"
    }
}
