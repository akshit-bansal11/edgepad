package me.akshitbansal.edgepad.screens

import android.text.TextUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.NetBand
import me.akshitbansal.edgepad.link.NetKind
import me.akshitbansal.edgepad.link.SignalLevel
import java.text.NumberFormat

private const val CHIP_RADIUS_DP = 14f
private const val CHIP_PAD_DP = 10f
private const val CHIP_ICON_DP = 20f
private const val CHIP_NAME_MAX_DP = 120f
private const val CHEVRON_TURN = 90f
private const val KV_ROW_DP = 36f
private const val METER_DP = 6f
private const val WHOLE = 100
private const val GBPS_FROM = 1000
private const val SIGNAL_STRONG_FROM = 75
private const val SIGNAL_GOOD_FROM = 50
private const val SIGNAL_FAIR_FROM = 25

/** The laptop's name with a chevron, at the end of a hub's nav bar; a tap opens the device switcher. */
internal fun Ui.laptopChip(
    name: String,
    onSwitch: () -> Unit,
): View {
    val slot = dp(CHIP_ICON_DP)
    val nameParams =
        LinearLayout
            .LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .also { it.marginStart = dp(Space.XS) }
    val pill =
        LinearLayout(context).apply {
            gravity = Gravity.CENTER_VERTICAL
            background = rounded(palette.faint, CHIP_RADIUS_DP)
            setPadding(dp(CHIP_PAD_DP), dp(Space.XS), dp(CHIP_PAD_DP), dp(Space.XS))
            addView(Glyph(context, Glyph.Shape.LAPTOP, palette.dim), LinearLayout.LayoutParams(slot, slot))
            addView(
                text(name, Type.SMALL, palette.ink, face = Type.bold).apply {
                    maxLines = 1
                    ellipsize = TextUtils.TruncateAt.END
                    maxWidth = dp(CHIP_NAME_MAX_DP)
                },
                nameParams,
            )
            addView(
                Glyph(context, Glyph.Shape.CHEVRON_RIGHT, palette.dim).apply { rotation = CHEVRON_TURN },
                LinearLayout.LayoutParams(slot, slot),
            )
        }
    return LinearLayout(context).apply {
        gravity = Gravity.CENTER_VERTICAL
        minimumHeight = dp(Space.TOUCH)
        setPaddingRelative(dp(Space.S), 0, dp(Space.S), 0)
        contentDescription = string(R.string.hub_switch_laptop, name)
        addView(pill)
        tappable(this, Space.M, onSwitch)
    }
}

/** A hub page's nav bar: Back to Controls, the hub's [title] and the laptop chip. */
internal fun Ui.hubBar(
    title: String,
    laptopName: String,
    onSwitch: () -> Unit,
    onBack: () -> Unit,
): View = bar(title, onBack, laptopChip(laptopName, onSwitch), backLabel = string(R.string.back_to_surface))

/** [pages] side by side in a [Pager] with its [PageDots] under it; [onPage] is told the page that shows. */
internal fun Ui.hubView(
    pages: List<View>,
    onPage: (Int) -> Unit,
): View {
    val dots = PageDots(this, pages.size)
    val pager =
        Pager(context) { at ->
            dots.select(at)
            onPage(at)
        }
    pages.forEach { pager.row.addView(it, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT)) }
    // Each page is as wide as the pager, which is not known until it is laid out.
    pager.addOnLayoutChangeListener { _, left, _, right, _, _, _, _, _ ->
        val wide = right - left
        for (i in 0 until pager.row.childCount) {
            val page = pager.row.getChildAt(i)
            if (page.layoutParams.width != wide) {
                page.layoutParams = page.layoutParams.apply { width = wide }
            }
        }
    }
    return LinearLayout(context).apply {
        orientation = LinearLayout.VERTICAL
        addView(pager, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
        addView(dots, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(Space.XL)))
    }
}

/** A dim label and its value in bold at the far side, 36 dp high. */
internal fun Column.kv(
    label: String,
    value: String,
) {
    val row = ui.row(ui.secondary(label, Type.VALUE), ui.text(value, Type.VALUE, ui.palette.ink, face = Type.bold))
    row.minimumHeight = ui.dp(KV_ROW_DP)
    add(row)
}

/** A 6 dp bar filled to [pct] percent in [color], named [name] for a screen reader. */
internal fun Column.meter(
    name: String,
    pct: Int,
    color: Int = ui.palette.accent,
) {
    val share = pct.coerceIn(0, WHOLE)
    val fill = View(ui.context).apply { background = ui.rounded(color, Space.CARD_RADIUS) }
    val track =
        LinearLayout(ui.context).apply {
            weightSum = WHOLE.toFloat()
            background = ui.rounded(ui.palette.track, Space.CARD_RADIUS)
            contentDescription = "$name, $share%"
            addView(fill, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, share.toFloat()))
            addView(
                View(ui.context),
                LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, (WHOLE - share).toFloat()),
            )
        }
    add(track, height = ui.dp(METER_DP))
}

/** The words for a connection: its kind and band, link speed and signal. A value not known reads as a dash. */
internal class NetText(
    private val ui: Ui,
) {
    private val none = ui.string(R.string.sys_none)
    private val oneDecimal = NumberFormat.getNumberInstance().apply { maximumFractionDigits = 1 }

    fun joined(
        first: String,
        second: String,
    ): String = ui.string(R.string.net_join, first, second)

    fun millis(ms: Int): String = if (ms < 0) none else ui.string(R.string.net_ms, ms)

    fun decimal(value: Double): String = oneDecimal.format(value)

    /** "Wi-Fi · 5 GHz", or just the kind when there is no band. */
    fun connection(
        kind: NetKind,
        band: NetBand,
    ): String {
        val name =
            ui.string(
                when (kind) {
                    NetKind.WIFI -> R.string.net_wifi
                    NetKind.ETHERNET -> R.string.net_ethernet
                    NetKind.CELLULAR -> R.string.net_cellular
                    NetKind.OTHER -> R.string.net_other
                    NetKind.NONE -> R.string.net_none
                },
            )
        return if (kind == NetKind.WIFI && band != NetBand.UNKNOWN) {
            joined(name, ui.string(R.string.net_ghz, band.token))
        } else {
            name
        }
    }

    fun linkSpeed(mbps: Int): String =
        when {
            mbps < 0 -> none
            mbps >= GBPS_FROM -> ui.string(R.string.net_gbps, decimal(mbps / GBPS_FROM.toDouble()))
            else -> ui.string(R.string.net_mbps, decimal(mbps.toDouble()))
        }

    fun signal(level: SignalLevel): String =
        when (level) {
            SignalLevel.EXCELLENT -> ui.string(R.string.net_signal_strong)
            SignalLevel.GOOD -> ui.string(R.string.net_signal_good)
            SignalLevel.FAIR -> ui.string(R.string.net_signal_fair)
            SignalLevel.WEAK -> ui.string(R.string.net_signal_weak)
            SignalLevel.UNKNOWN -> none
        }

    /** The laptop's signal as a percent, -1 when it does not say (Windows never does). */
    fun signal(pct: Int): String =
        when {
            pct < 0 -> none
            pct >= SIGNAL_STRONG_FROM -> signal(SignalLevel.EXCELLENT)
            pct >= SIGNAL_GOOD_FROM -> signal(SignalLevel.GOOD)
            pct >= SIGNAL_FAIR_FROM -> signal(SignalLevel.FAIR)
            else -> signal(SignalLevel.WEAK)
        }

    fun address(ip: String): String = ip.ifEmpty { none }
}
