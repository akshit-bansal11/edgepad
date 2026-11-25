package me.akshitbansal.edgepad.screens

import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.Laptop
import me.akshitbansal.edgepad.link.LaptopConnections
import me.akshitbansal.edgepad.link.PhoneBattery
import me.akshitbansal.edgepad.protocol.BatteryKind
import me.akshitbansal.edgepad.surface.BatteryGroup
import me.akshitbansal.edgepad.surface.BatteryModel
import me.akshitbansal.edgepad.surface.BatteryRow
import me.akshitbansal.edgepad.surface.BatterySource
import me.akshitbansal.edgepad.surface.LaptopView

private const val BAR_WIDTH_DP = 56f
private const val BAR_HEIGHT_DP = 6f
private const val PERCENT_WIDTH_DP = 44f
private const val FULL = 100
private const val ICON_DP = 24f

/**
 * The batteries the phone can see now: the laptop in use first, each other connected laptop under its name,
 * then this phone. Read on demand, so a refresh is a fresh call.
 */
class LiveBatteries(
    private val manager: LaptopConnections,
    private val phone: PhoneBattery,
    private val phoneName: String,
) : BatterySource {
    override fun groups() =
        BatteryModel.build(
            manager.selected?.takeIf { it.connected }?.let(::view),
            manager.laptops.filter { it.connected && it !== manager.selected }.map(::view),
            phone.read()?.let { BatteryRow(phoneName, null, true, it.level, it.charging, phoneName) },
        )

    private fun view(l: Laptop) = LaptopView(l.state.laptopId ?: l.id, l.name, l.state.batteries)
}

/**
 * The battery list as a modal over the surface: same card and window as [ControlsPopup], scrolling when the
 * screen is short. [refresh] rebuilds the rows while it is open; Back or a tap outside closes it.
 */
class BatterySheet(
    ui: Ui,
    private val source: BatterySource,
) : ListSheet(ui, R.string.battery_title, reveal = false) {
    /** A battery changed: the rows are made again from what the source says now. */
    fun refresh() {
        if (isOpen) fill()
    }

    override fun fill() {
        list.removeAllViews()
        val g = source.groups()
        g.inUse?.let { group(ui.string(R.string.battery_in_use), it) }
        g.others.forEach { group(it.name, it) }
        g.phone?.let { group(ui.string(R.string.battery_this_phone), BatteryGroup("", listOf(it))) }
    }

    private fun group(
        title: String,
        group: BatteryGroup,
    ) {
        list.addView(ui.section(title).apply { setPadding(0, ui.dp(Space.S), 0, ui.dp(Space.S)) })
        group.rows.forEach { list.addView(row(it)) }
    }

    private fun row(r: BatteryRow): View {
        val p = ui.palette
        val color = if (r.low) p.danger else p.accent
        val name = ui.text(r.name, Type.BODY, p.ink).apply { setSingleLine() }
        val tag =
            when {
                r.charging -> ui.string(R.string.battery_charging)
                r.low -> ui.string(R.string.battery_low)
                else -> null
            }
        val left =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(name)
                tag?.let { addView(ui.secondary(it, Type.SMALL, if (r.low) p.danger else p.dim)) }
            }
        val bar =
            LinearLayout(ui.context).apply {
                background = ui.rounded(p.line, BAR_HEIGHT_DP)
                addView(View(ui.context).apply { background = ui.rounded(color, BAR_HEIGHT_DP) }, weight(r.level))
                addView(View(ui.context), weight(FULL - r.level))
            }
        val percent =
            ui.text(ui.string(R.string.battery_percent, r.level), Type.VALUE, color).apply {
                gravity = Gravity.END
            }
        val icon =
            ImageView(ui.context).apply {
                setImageResource(iconOf(r))
                setColorFilter(p.ink)
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
            }
        return ui.row(left).apply {
            addView(
                icon,
                0,
                LinearLayout.LayoutParams(ui.dp(ICON_DP), ui.dp(ICON_DP)).apply { marginEnd = ui.dp(Space.M) },
            )
            addView(bar, LinearLayout.LayoutParams(ui.dp(BAR_WIDTH_DP), ui.dp(BAR_HEIGHT_DP)))
            addView(percent, LinearLayout.LayoutParams(ui.dp(PERCENT_WIDTH_DP), ViewGroup.LayoutParams.WRAP_CONTENT))
        }
    }

    private fun weight(w: Int) = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, w.toFloat())
}

private fun iconOf(r: BatteryRow): Int =
    if (r.isPhone) {
        R.drawable.ic_battery_phone
    } else {
        when (r.kind) {
            BatteryKind.LAPTOP -> R.drawable.ic_battery_laptop
            BatteryKind.HEADPHONES -> R.drawable.ic_battery_headphones
            BatteryKind.KEYBOARD -> R.drawable.ic_battery_keyboard
            BatteryKind.MOUSE -> R.drawable.ic_battery_mouse
            BatteryKind.SPEAKER -> R.drawable.ic_battery_speaker
            else -> R.drawable.ic_battery_other
        }
    }
