package me.akshitbansal.edgepad.screens

import android.view.View
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.BatteryHealth
import me.akshitbansal.edgepad.link.ByteRatio
import me.akshitbansal.edgepad.link.PhoneBatteryInfo
import me.akshitbansal.edgepad.link.PhoneSnapshot
import me.akshitbansal.edgepad.link.StatsFormat
import me.akshitbansal.edgepad.link.ThermalLevel

/**
 * The Phone page of System: this phone's battery, memory and storage, its connection and how hot it is. The numbers
 * are the phone's own, read by [me.akshitbansal.edgepad.link.PhoneStats] only while the page shows; [show] draws what
 * it last read, with a dash for what it has not read yet. Nothing here leaves the phone.
 */
class PhoneScreen(
    private val ui: Ui,
    bar: View,
) {
    private val none = ui.string(R.string.sys_none)
    private val fmt = StatsFormat(none)
    private val net = NetText(ui)
    private val holder = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }

    val view: View = ui.page(bar) { add(holder) }

    init {
        show(PhoneSnapshot())
    }

    /** Draws [snapshot] in place of what was shown. */
    fun show(snapshot: PhoneSnapshot) {
        holder.removeAllViews()
        Column(ui, holder).render(snapshot)
    }

    private fun Column.render(s: PhoneSnapshot) {
        columns(
            first = { batteryCard(s.battery) },
            second = {
                usageCard(s)
                connectionCard(s)
            },
        )
    }

    /** The charge large, whether it is charging, a bar, and the temperature and health the battery reports. */
    private fun Column.batteryCard(battery: PhoneBatteryInfo?) {
        card(Space.M) {
            val level = battery?.level ?: -1
            add(ui.text(ui.string(R.string.phone_battery), Type.BODY, ui.palette.ink, face = Type.bold), Space.M)
            val charge = if (level < 0) none else ui.string(R.string.battery_percent, level)
            add(ui.text(charge, Type.LARGE_TITLE, ui.palette.ink, Type.TRACKING_TIGHT, Type.black), Space.S)
            if (battery?.charging == true) {
                add(ui.text(ui.string(R.string.phone_charging), Type.SMALL, ui.palette.accent, face = Type.bold))
            }
            meter(ui.string(R.string.phone_battery), level.coerceAtLeast(0), batteryColor(battery))
            add(View(ui.context), height = ui.dp(Space.S))
            kv(ui.string(R.string.phone_temperature), temperature(battery?.tempTenthsC ?: -1))
            kv(ui.string(R.string.phone_health), health(battery?.health ?: BatteryHealth.UNKNOWN))
            add(View(ui.context), height = ui.dp(Space.S))
        }
    }

    private fun batteryColor(battery: PhoneBatteryInfo?): Int =
        if (battery != null && battery.level <= LOW && !battery.charging) ui.palette.danger else ui.palette.accent

    /** Memory and storage, each a title, what is used of the total and a bar. */
    private fun Column.usageCard(s: PhoneSnapshot) {
        card(Space.M) {
            add(View(ui.context), height = ui.dp(Space.S))
            usage(ui.string(R.string.sys_ram), s.memory)
            add(View(ui.context), height = ui.dp(Space.M))
            usage(ui.string(R.string.phone_storage), s.storage)
            add(View(ui.context), height = ui.dp(Space.M))
        }
    }

    private fun Column.usage(
        title: String,
        reading: ByteRatio?,
    ) {
        val ratio = reading?.takeIf { it.totalBytes > 0 && it.usedBytes >= 0 }
        val text =
            if (ratio == null) {
                none
            } else {
                ui.string(R.string.phone_used_of, fmt.gib(mib(ratio.usedBytes)), fmt.gib(mib(ratio.totalBytes)))
            }
        add(ui.row(ui.text(title, Type.BODY, ui.palette.ink, face = Type.bold), ui.secondary(text, Type.VALUE)))
        val pct = if (ratio == null) 0 else (ratio.usedBytes * FULL / ratio.totalBytes).toInt()
        meter(title, pct, if (pct >= DANGER_FROM) ui.palette.danger else ui.palette.accent)
    }

    private fun Column.connectionCard(s: PhoneSnapshot) {
        card(Space.M) {
            val phone = s.net
            kv(ui.string(R.string.net_connection), phone?.let { net.connection(it.kind, it.band) } ?: none)
            hairline()
            kv(ui.string(R.string.phone_thermal), thermal(s.thermal))
        }
    }

    private fun mib(bytes: Long): Int = (bytes / BYTES_PER_MIB).toInt()

    private fun temperature(tenths: Int): String =
        if (tenths < 0) none else ui.string(R.string.phone_celsius, net.decimal(tenths / TENTHS))

    private fun health(health: BatteryHealth): String =
        when (health) {
            BatteryHealth.GOOD -> ui.string(R.string.phone_health_good)
            BatteryHealth.OVERHEAT -> ui.string(R.string.phone_health_hot)
            BatteryHealth.DEAD -> ui.string(R.string.phone_health_worn)
            BatteryHealth.OVER_VOLTAGE -> ui.string(R.string.phone_health_voltage)
            BatteryHealth.FAILURE -> ui.string(R.string.phone_health_fault)
            BatteryHealth.COLD -> ui.string(R.string.phone_health_cold)
            BatteryHealth.UNKNOWN -> none
        }

    private fun thermal(level: ThermalLevel): String =
        when (level) {
            ThermalLevel.NONE -> ui.string(R.string.phone_thermal_none)
            ThermalLevel.LIGHT -> ui.string(R.string.phone_thermal_light)
            ThermalLevel.MODERATE -> ui.string(R.string.phone_thermal_moderate)
            ThermalLevel.SEVERE -> ui.string(R.string.phone_thermal_severe)
            ThermalLevel.UNKNOWN -> none
            else -> ui.string(R.string.phone_thermal_critical)
        }

    private companion object {
        const val FULL = 100
        const val LOW = 20
        const val DANGER_FROM = 90
        const val BYTES_PER_MIB = 1_048_576L
        const val TENTHS = 10.0
    }
}
