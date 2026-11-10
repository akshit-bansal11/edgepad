package me.akshitbansal.edgepad.link

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.BatteryManager

/** This phone's own battery, read from the sticky battery broadcast. Not a wire kind: the surface models it apart. */
class PhoneBattery(private val context: Context) {
    private val filter = IntentFilter(Intent.ACTION_BATTERY_CHANGED)

    /** Level 0-100 and charging, or null when the system reports no level. */
    fun read(): PhoneLevel? {
        val i = context.registerReceiver(null, filter) ?: return null
        val level = i.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
        val scale = i.getIntExtra(BatteryManager.EXTRA_SCALE, -1)
        if (level < 0 || scale <= 0) return null
        val status = i.getIntExtra(BatteryManager.EXTRA_STATUS, -1)
        val charging = status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL
        return PhoneLevel((level * 100 / scale).coerceIn(0, 100), charging)
    }

    /** Calls [onChange] on every battery broadcast until the result is closed. */
    fun watch(onChange: () -> Unit): AutoCloseable {
        val receiver =
            object : BroadcastReceiver() {
                override fun onReceive(c: Context?, intent: Intent?) = onChange()
            }
        context.registerReceiver(receiver, filter)
        return AutoCloseable { context.unregisterReceiver(receiver) }
    }
}

/** The phone's level and whether it is charging. */
data class PhoneLevel(val level: Int, val charging: Boolean)
