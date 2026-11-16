package me.akshitbansal.edgepad.link

import android.app.PendingIntent
import android.content.Intent
import android.os.Build
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import me.akshitbansal.edgepad.MainActivity
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.protocol.ActionId

/** What the tile shows for [status]: lit with the laptop's name, or dark when nothing is connected. */
internal data class TileModel(
    val active: Boolean,
    val name: String?,
) {
    companion object {
        fun of(status: TileStatus?): TileModel =
            if (status == null) {
                TileModel(false, null)
            } else {
                TileModel(true, status.laptopName.takeIf { it.isNotEmpty() })
            }
    }
}

/**
 * The Quick Settings tile: tap plays or pauses on the selected laptop, or opens the app when none is connected.
 * Bound by the system only while the shade shows it; it reads the state when it appears and again on a tap, and
 * runs nothing in between. It works without the notification permission.
 */
class EdgepadTile : TileService() {
    override fun onStartListening() {
        val tile = qsTile ?: return
        val model = TileModel.of(LinkHooks.tileStatus?.invoke())
        tile.state = if (model.active) Tile.STATE_ACTIVE else Tile.STATE_INACTIVE
        tile.subtitle = model.name ?: getString(R.string.tile_subtitle_off)
        tile.updateTile()
    }

    override fun onClick() {
        if (LinkHooks.sendToSelected?.invoke(ActionId.PLAY_PAUSE.frame()) == true) return
        val open = Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startActivityAndCollapse(
                PendingIntent.getActivity(this, 0, open, PendingIntent.FLAG_IMMUTABLE),
            )
        } else {
            // NOTE: the PendingIntent overload exists only from API 34; minSdk is 31
            @Suppress("DEPRECATION")
            startActivityAndCollapse(open)
        }
    }
}
