package me.akshitbansal.edgepad.screens

import android.os.Build
import android.text.format.DateUtils
import android.text.format.Formatter
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.PhoneSnapshot
import me.akshitbansal.edgepad.link.SignalLevel
import me.akshitbansal.edgepad.link.SpeedResult
import me.akshitbansal.edgepad.link.SpeedStrip
import me.akshitbansal.edgepad.link.StatsRing
import me.akshitbansal.edgepad.link.StripKind

/**
 * The Network page of System: the laptop's connection and the phone's side by side (stacked upright), then a speed
 * test of the laptop. No network name is shown anywhere. The laptop sends its part only while this page shows, and
 * the phone's numbers are read only then too; the activity calls [refresh] when either changed. The test runs on the
 * laptop, only after a tap on the button, and [onSpeed] says so with true to start and false to stop.
 *
 * @param state The laptop this page shows, read afresh on each [refresh].
 * @param rtt The round trip to the laptop in ms, -1 until measured.
 */
class NetworkScreen(
    private val ui: Ui,
    bar: View,
    private val state: () -> LaptopState,
    private val rtt: () -> Int,
    private val onSpeed: (Boolean) -> Unit,
) {
    private val none = ui.string(R.string.sys_none)
    private val net = NetText(ui)
    private val holder = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
    private var phone = PhoneSnapshot()

    val view: View = ui.page(bar) { add(holder) }

    init {
        refresh()
    }

    /** The phone read new numbers. */
    fun phoneChanged(snapshot: PhoneSnapshot) {
        phone = snapshot
        refresh()
    }

    /** Draws what the laptop and the phone have said so far. */
    fun refresh() {
        holder.removeAllViews()
        Column(ui, holder).render(state())
    }

    private fun Column.render(s: LaptopState) {
        columns(
            first = { laptopCard(s) },
            second = { phoneCard() },
        )
        speedCard(s.speed)
    }

    private fun Column.laptopCard(s: LaptopState) {
        card(Space.M) {
            val info = s.netInfo
            val ping = s.stats.latest(StatsRing.PING)
            val loss = s.stats.latest(StatsRing.LOSS)
            header(R.string.net_card_laptop, s.laptopName?.ifEmpty { none } ?: none)
            kv(ui.string(R.string.net_connection), info?.let { net.connection(it.kind, it.band) } ?: none)
            kv(ui.string(R.string.net_link_speed), net.linkSpeed(info?.linkMbps ?: -1))
            kv(ui.string(R.string.net_signal), net.signal(info?.signalPct ?: -1))
            kv(ui.string(R.string.net_local_ip), net.address(info?.localIp.orEmpty()))
            val pingText = if (ping < 0 || loss < 0) none else routerText(ping, loss)
            kv(ui.string(R.string.net_router), pingText)
            add(View(ui.context), height = ui.dp(Space.S))
        }
    }

    private fun routerText(
        ping: Int,
        loss: Int,
    ): String = net.joined(net.millis(ping), ui.string(R.string.net_loss, loss))

    private fun Column.phoneCard() {
        card(Space.M) {
            val link = phone.net
            header(R.string.net_card_phone, Build.MODEL)
            kv(ui.string(R.string.net_connection), link?.let { net.connection(it.kind, it.band) } ?: none)
            kv(ui.string(R.string.net_link_speed), net.linkSpeed(link?.linkMbps ?: -1))
            kv(ui.string(R.string.net_signal), net.signal(link?.signal ?: SignalLevel.UNKNOWN))
            kv(ui.string(R.string.net_local_ip), net.address(link?.ipv4.orEmpty()))
            val ms = rtt()
            val laptopLink =
                if (link == null || ms < 0) {
                    none
                } else {
                    net.joined(net.connection(link.kind, link.band), net.millis(ms))
                }
            kv(ui.string(R.string.net_link_laptop), laptopLink)
            add(View(ui.context), height = ui.dp(Space.S))
        }
    }

    private fun Column.header(
        title: Int,
        name: String,
    ) {
        add(ui.text(ui.string(title, name), Type.BODY, ui.palette.ink, face = Type.bold), Space.M)
        add(View(ui.context), height = ui.dp(Space.XS))
    }

    /** The speed test of the laptop: its last result, one button that starts or stops it, and what a test costs. */
    private fun Column.speedCard(speed: SpeedResult?) {
        val running = SpeedStrip.running(speed)
        card(Space.M) {
            val title = ui.text(ui.string(R.string.net_speed_title), Type.BODY, ui.palette.ink, face = Type.bold)
            val button =
                ui.button(
                    ui.string(if (running) R.string.net_speed_stop else R.string.net_speed_start),
                    Ui.Style.QUIET,
                ) { onSpeed(!running) }
            add(ui.row(title, button))
            results(speed)
            add(View(ui.context), height = ui.dp(Space.M))
        }
        footnote(ui.string(R.string.net_speed_note, Formatter.formatShortFileSize(ui.context, SPEED_CAP_BYTES)))
    }

    private fun Column.results(speed: SpeedResult?) {
        val note =
            when (SpeedStrip.kind(speed)) {
                StripKind.IDLE -> R.string.net_speed_none
                StripKind.RUNNING -> R.string.net_speed_running
                StripKind.FAILED -> R.string.net_speed_failed
                StripKind.STOPPED -> R.string.net_speed_stopped
                StripKind.DONE -> 0
            }
        if (speed == null || note != 0) {
            secondary(ui.string(note), Type.VALUE)
            return
        }
        val jitter = ui.string(R.string.net_jitter, speed.jitterMs.coerceAtLeast(0))
        val line =
            LinearLayout(ui.context).apply {
                addView(figure(mbps(speed.downKbps), ui.string(R.string.net_unit_down)), weighted())
                addView(figure(mbps(speed.upKbps), ui.string(R.string.net_unit_up)), weighted())
                addView(figure(net.millis(speed.latencyMs), jitter), weighted())
            }
        add(line, Space.S)
        val time = DateUtils.formatDateTime(ui.context, speed.atUnixMin * MS_PER_MIN, DateUtils.FORMAT_SHOW_TIME)
        val used = Formatter.formatShortFileSize(ui.context, speed.bytes)
        secondary(net.joined(time, ui.string(R.string.net_speed_used, used)), Type.SMALL, topDp = Space.S)
    }

    private fun mbps(kbps: Long): String = if (kbps < 0) none else net.decimal(kbps / KBPS_PER_MBPS)

    /** A figure large with its caption under it. */
    private fun figure(
        value: String,
        caption: String,
    ): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER_HORIZONTAL
            addView(ui.text(value, Type.TITLE, ui.palette.ink, Type.TRACKING_TIGHT, Type.black))
            addView(ui.secondary(caption, Type.MICRO))
        }

    private fun weighted() = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)

    private companion object {
        const val SPEED_CAP_BYTES = 180_000_000L
        const val MS_PER_MIN = 60_000L
        const val KBPS_PER_MBPS = 1000.0
    }
}
