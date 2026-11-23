package me.akshitbansal.edgepad.link

import android.app.ActivityManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.ConnectivityManager
import android.net.LinkProperties
import android.net.Network
import android.net.NetworkCapabilities
import android.net.wifi.WifiInfo
import android.os.BatteryManager
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.os.StatFs
import java.net.Inet4Address

/** Battery health, from EXTRA_HEALTH. */
enum class BatteryHealth { GOOD, OVERHEAT, DEAD, OVER_VOLTAGE, FAILURE, COLD, UNKNOWN }

/** Thermal status, from [PowerManager.getCurrentThermalStatus]. */
enum class ThermalLevel { NONE, LIGHT, MODERATE, SEVERE, CRITICAL, EMERGENCY, SHUTDOWN, UNKNOWN }

/** How strong a Wi-Fi signal reads, from its RSSI in dBm. */
enum class SignalLevel { EXCELLENT, GOOD, FAIR, WEAK, UNKNOWN }

/** This phone's battery. [tempTenthsC] is -1 when unknown. */
data class PhoneBatteryInfo(
    val level: Int,
    val charging: Boolean,
    val tempTenthsC: Int,
    val health: BatteryHealth,
)

/** A used/total pair in bytes; both -1 when unknown. */
data class ByteRatio(
    val usedBytes: Long,
    val totalBytes: Long,
)

/** This phone's own connection. Never a network name. [linkMbps] is -1 when unknown, [ipv4] empty when none. */
data class PhoneNet(
    val kind: NetKind,
    val band: NetBand,
    val linkMbps: Int,
    val signal: SignalLevel,
    val ipv4: String,
)

/** Everything [PhoneStats] knows right now; null fields are not read yet. */
data class PhoneSnapshot(
    val battery: PhoneBatteryInfo? = null,
    val memory: ByteRatio? = null,
    val storage: ByteRatio? = null,
    val net: PhoneNet? = null,
    val thermal: ThermalLevel = ThermalLevel.UNKNOWN,
)

/** Pure mappings from system numbers to the tokens a screen names. */
object PhoneStatsFormat {
    private const val INVALID_RSSI = -127
    private const val RSSI_EXCELLENT = -55
    private const val RSSI_GOOD = -67
    private const val RSSI_FAIR = -80
    private const val BAND_2_4_FROM = 2400
    private const val BAND_2_4_TO = 2500
    private const val BAND_5_FROM = 5150
    private const val BAND_5_TO = 5895
    private const val BAND_6_FROM = 5925
    private const val BAND_6_TO = 7125

    fun bandOf(frequencyMhz: Int): NetBand =
        when (frequencyMhz) {
            in BAND_2_4_FROM..BAND_2_4_TO -> NetBand.GHZ_2_4
            in BAND_5_FROM..BAND_5_TO -> NetBand.GHZ_5
            in BAND_6_FROM..BAND_6_TO -> NetBand.GHZ_6
            else -> NetBand.UNKNOWN
        }

    fun signalOf(rssiDbm: Int): SignalLevel =
        when {
            rssiDbm == INVALID_RSSI || rssiDbm >= 0 -> SignalLevel.UNKNOWN
            rssiDbm >= RSSI_EXCELLENT -> SignalLevel.EXCELLENT
            rssiDbm >= RSSI_GOOD -> SignalLevel.GOOD
            rssiDbm >= RSSI_FAIR -> SignalLevel.FAIR
            else -> SignalLevel.WEAK
        }

    fun thermalOf(status: Int): ThermalLevel =
        when (status) {
            PowerManager.THERMAL_STATUS_NONE -> ThermalLevel.NONE
            PowerManager.THERMAL_STATUS_LIGHT -> ThermalLevel.LIGHT
            PowerManager.THERMAL_STATUS_MODERATE -> ThermalLevel.MODERATE
            PowerManager.THERMAL_STATUS_SEVERE -> ThermalLevel.SEVERE
            PowerManager.THERMAL_STATUS_CRITICAL -> ThermalLevel.CRITICAL
            PowerManager.THERMAL_STATUS_EMERGENCY -> ThermalLevel.EMERGENCY
            PowerManager.THERMAL_STATUS_SHUTDOWN -> ThermalLevel.SHUTDOWN
            else -> ThermalLevel.UNKNOWN
        }

    fun healthOf(health: Int): BatteryHealth =
        when (health) {
            BatteryManager.BATTERY_HEALTH_GOOD -> BatteryHealth.GOOD
            BatteryManager.BATTERY_HEALTH_OVERHEAT -> BatteryHealth.OVERHEAT
            BatteryManager.BATTERY_HEALTH_DEAD -> BatteryHealth.DEAD
            BatteryManager.BATTERY_HEALTH_OVER_VOLTAGE -> BatteryHealth.OVER_VOLTAGE
            BatteryManager.BATTERY_HEALTH_UNSPECIFIED_FAILURE -> BatteryHealth.FAILURE
            BatteryManager.BATTERY_HEALTH_COLD -> BatteryHealth.COLD
            else -> BatteryHealth.UNKNOWN
        }
}

/**
 * Starts every registration on [observe] and closes every one on [stop]; nothing is registered otherwise. Pure, so a
 * JVM test holds the lifecycle over fake registrations.
 */
class Observation(
    private val registrations: List<() -> AutoCloseable>,
) {
    private var open: List<AutoCloseable> = emptyList()

    val observing: Boolean get() = open.isNotEmpty()

    fun observe() {
        if (observing) return
        open = registrations.map { it() }
    }

    fun stop() {
        val closing = open
        open = emptyList()
        for (c in closing.asReversed()) c.close()
    }
}

/**
 * The phone's own numbers for the Phone and Network pages. [observe] starts the battery receiver, the network callback
 * and the thermal listener, and a read of memory and storage every [POLL_MS]; [stop] ends all of them. Nothing runs
 * between. The network name and the access point's address are never read. Callbacks run on the main thread.
 */
class PhoneStats(
    private val context: Context,
    private val onChange: (PhoneSnapshot) -> Unit,
) {
    var snapshot = PhoneSnapshot()
        private set

    private val handler = Handler(Looper.getMainLooper())
    private val observation = Observation(listOf(::watchBattery, ::watchNetwork, ::watchThermal, ::watchPoll))

    fun observe() = observation.observe()

    fun stop() = observation.stop()

    private fun update(next: PhoneSnapshot) {
        snapshot = next
        onChange(next)
    }

    private fun watchBattery(): AutoCloseable {
        val receiver =
            object : BroadcastReceiver() {
                override fun onReceive(
                    c: Context?,
                    intent: Intent?,
                ) {
                    if (intent != null) readBattery(intent)
                }
            }
        // The sticky intent comes back from the same call that registers, so there is no separate first read.
        context.registerReceiver(receiver, IntentFilter(Intent.ACTION_BATTERY_CHANGED))?.let(::readBattery)
        return AutoCloseable { context.unregisterReceiver(receiver) }
    }

    private fun readBattery(i: Intent) {
        val level = i.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
        val scale = i.getIntExtra(BatteryManager.EXTRA_SCALE, -1)
        if (level < 0 || scale <= 0) return
        val status = i.getIntExtra(BatteryManager.EXTRA_STATUS, -1)
        val info =
            PhoneBatteryInfo(
                (level * PERCENT / scale).coerceIn(0, PERCENT),
                status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL,
                i.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, -1),
                PhoneStatsFormat.healthOf(i.getIntExtra(BatteryManager.EXTRA_HEALTH, -1)),
            )
        update(snapshot.copy(battery = info))
    }

    private fun watchNetwork(): AutoCloseable {
        val cm = context.getSystemService(ConnectivityManager::class.java)
        var ip = ""
        val callback =
            object : ConnectivityManager.NetworkCallback() {
                override fun onCapabilitiesChanged(
                    network: Network,
                    caps: NetworkCapabilities,
                ) = update(snapshot.copy(net = netOf(caps, ip)))

                override fun onLinkPropertiesChanged(
                    network: Network,
                    props: LinkProperties,
                ) {
                    ip = props.linkAddresses.firstNotNullOfOrNull { (it.address as? Inet4Address)?.hostAddress } ?: ""
                    snapshot.net?.let { update(snapshot.copy(net = it.copy(ipv4 = ip))) }
                }

                override fun onLost(network: Network) {
                    ip = ""
                    update(snapshot.copy(net = PhoneNet(NetKind.NONE, NetBand.UNKNOWN, -1, SignalLevel.UNKNOWN, "")))
                }
            }
        cm.registerDefaultNetworkCallback(callback, handler)
        return AutoCloseable { cm.unregisterNetworkCallback(callback) }
    }

    private fun netOf(
        caps: NetworkCapabilities,
        ip: String,
    ): PhoneNet {
        val kind =
            when {
                caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) -> NetKind.WIFI
                caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET) -> NetKind.ETHERNET
                caps.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR) -> NetKind.CELLULAR
                else -> NetKind.OTHER
            }
        // Only link speed, frequency and RSSI come from WifiInfo: none needs Location, and getSSID is never called.
        val wifi = caps.transportInfo as? WifiInfo
        val estimateMbps = caps.linkDownstreamBandwidthKbps.takeIf { it > 0 }?.div(KBPS_PER_MBPS) ?: -1
        return PhoneNet(
            kind,
            wifi?.let { PhoneStatsFormat.bandOf(it.frequency) } ?: NetBand.UNKNOWN,
            wifi?.linkSpeed?.takeIf { it > 0 } ?: estimateMbps,
            wifi?.let { PhoneStatsFormat.signalOf(it.rssi) } ?: SignalLevel.UNKNOWN,
            ip,
        )
    }

    private fun watchThermal(): AutoCloseable {
        val pm = context.getSystemService(PowerManager::class.java)
        val listener =
            PowerManager.OnThermalStatusChangedListener {
                update(snapshot.copy(thermal = PhoneStatsFormat.thermalOf(it)))
            }
        pm.addThermalStatusListener(context.mainExecutor, listener)
        update(snapshot.copy(thermal = PhoneStatsFormat.thermalOf(pm.currentThermalStatus)))
        return AutoCloseable { pm.removeThermalStatusListener(listener) }
    }

    private fun watchPoll(): AutoCloseable {
        val tick =
            object : Runnable {
                override fun run() {
                    readMemoryAndStorage()
                    handler.postDelayed(this, POLL_MS)
                }
            }
        tick.run()
        return AutoCloseable { handler.removeCallbacks(tick) }
    }

    private fun readMemoryAndStorage() {
        val info = ActivityManager.MemoryInfo()
        context.getSystemService(ActivityManager::class.java).getMemoryInfo(info)
        val stat = StatFs(context.dataDir.path)
        update(
            snapshot.copy(
                memory = ByteRatio(info.totalMem - info.availMem, info.totalMem),
                storage = ByteRatio(stat.totalBytes - stat.availableBytes, stat.totalBytes),
            ),
        )
    }

    private companion object {
        const val POLL_MS = 5_000L
        const val PERCENT = 100
        const val KBPS_PER_MBPS = 1_000
    }
}
