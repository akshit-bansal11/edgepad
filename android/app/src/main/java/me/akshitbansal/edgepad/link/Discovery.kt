package me.akshitbansal.edgepad.link

import android.annotation.TargetApi
import android.content.Context
import android.content.pm.PackageManager
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.os.Build
import android.util.Log
import me.akshitbansal.edgepad.link.net.LinkWire
import java.net.InetAddress
import java.util.concurrent.Executor

/** How a laptop in the Devices list is reached; the list shows it as a chip on the row. */
enum class Transport { BLUETOOTH, WIFI, USB }

/** A laptop found on the network, by mDNS ([Transport.WIFI]), the tether probe ([Transport.USB]) or by address. */
class Found(
    val laptopId: ByteArray,
    val name: String,
    val hosts: List<InetAddress>,
    val port: Int,
    val transport: Transport,
    /** The protocol version its announcement or INFO reply recorded, where it gave one. */
    val version: Int? = null,
) {
    val idHex: String get() = laptopId.joinToString("") { "%02x".format(it) }
}

/** What a laptop's mDNS TXT record says about it. */
class Txt(
    val laptopId: ByteArray,
    val name: String?,
    val version: Int?,
)

/**
 * The TXT record of an `_edgepad._tcp` service: `id` (32 hex digits), `n` (the name), `v` (the protocol version).
 * Anything on the LAN can announce one, so a record without a well-formed `id` is ignored, and a name is only
 * ever shown, never trusted. Pure, so a test covers it without NsdManager.
 */
internal fun parseTxt(attributes: Map<String, ByteArray?>): Txt? {
    val id =
        attributes["id"]
            ?.let { parseHex(String(it, Charsets.UTF_8)) }
            ?.takeIf { it.size == LinkWire.ID_LENGTH }
            ?: return null
    val name = attributes["n"]?.let { String(it, Charsets.UTF_8).trim().take(MAX_NAME) }?.takeIf { it.isNotEmpty() }
    val version = attributes["v"]?.let { String(it, Charsets.UTF_8).trim().toIntOrNull() }
    return Txt(id, name, version)
}

private const val MAX_NAME = 64

internal fun parseHex(text: String): ByteArray? {
    if (text.length % 2 != 0 || text.any { it !in '0'..'9' && it !in 'a'..'f' && it !in 'A'..'F' }) return null
    return ByteArray(text.length / 2) { text.substring(it * 2, it * 2 + 2).toInt(16).toByte() }
}

/**
 * One row per laptop per transport: the same laptop announced twice, or found on two addresses, is one Wi-Fi row
 * with the addresses together; the same laptop on Wi-Fi and over USB stays two rows, one to connect on each,
 * each marked by [otherTransport] with the other. Sorted by name, then transport.
 */
internal fun mergeFound(all: List<Found>): List<Found> =
    all
        .groupBy { it.idHex to it.transport }
        .values
        .map { group ->
            val first = group.first()
            Found(
                first.laptopId,
                first.name,
                group.flatMap { it.hosts }.distinct(),
                first.port,
                first.transport,
                group.firstNotNullOfOrNull { it.version },
            )
        }.sortedWith(compareBy({ it.name.lowercase() }, { it.transport }))

/** The other transport [row]'s laptop is also found on, if any. */
internal fun otherTransport(
    row: Found,
    all: List<Found>,
): Transport? = all.firstOrNull { it.idHex == row.idHex && it.transport != row.transport }?.transport

/**
 * Android 17 keeps an app targeting API 37 off the local network until it holds this runtime permission, of the
 * Nearby devices group (https://developer.android.com/privacy-and-security/local-network-permission). Earlier
 * Androids have no such permission: nothing to ask, nothing missing.
 */
object LocalNetwork {
    const val PERMISSION = "android.permission.ACCESS_LOCAL_NETWORK"
    private const val FIRST_SDK = 37

    val required: Boolean get() = Build.VERSION.SDK_INT >= FIRST_SDK

    fun granted(context: Context): Boolean =
        !required || context.checkSelfPermission(PERMISSION) == PackageManager.PERMISSION_GRANTED
}

/**
 * Browses `_edgepad._tcp` with NsdManager and reports every laptop it can resolve, as the whole current list, on
 * NsdManager's own thread ([onChange] must hop to the main thread itself). Android 14 and later watch each service
 * with a service-info callback, which keeps the addresses current; before that each is resolved once, one at a
 * time, because NsdManager refuses two resolves at once. Does nothing without the local-network permission.
 */
class Discovery(
    context: Context,
    private val onChange: (List<Found>) -> Unit,
) {
    private val appContext = context.applicationContext
    private val direct = Executor { it.run() }
    private val services = LinkedHashMap<String, Found>()
    private val watching = HashMap<String, () -> Unit>()
    private val queue = ArrayDeque<NsdServiceInfo>()
    private var resolving = false
    private var listener: NsdManager.DiscoveryListener? = null

    private val nsd: NsdManager? get() = appContext.getSystemService(NsdManager::class.java)

    @Synchronized
    fun start() {
        if (listener != null || !LocalNetwork.granted(appContext)) return
        val manager = nsd ?: return
        val l = browser()
        listener = l
        try {
            manager.discoverServices(SERVICE_TYPE, NsdManager.PROTOCOL_DNS_SD, l)
        } catch (e: SecurityException) {
            Log.w("Edgepad", "Not allowed to browse the local network", e)
            listener = null
        }
    }

    @Synchronized
    fun stop() {
        val l = listener ?: return
        listener = null
        val manager = nsd
        try {
            manager?.stopServiceDiscovery(l)
        } catch (e: IllegalArgumentException) {
            // Discovery never started or already ended; either way it is stopped.
        }
        watching.values.forEach {
            try {
                it()
            } catch (e: IllegalArgumentException) {
                // Already unregistered.
            }
        }
        watching.clear()
        queue.clear()
        resolving = false
        services.clear()
    }

    private fun browser() =
        object : NsdManager.DiscoveryListener {
            override fun onServiceFound(serviceInfo: NsdServiceInfo) {
                if (serviceInfo.serviceType.trimEnd('.') == SERVICE_TYPE) resolve(serviceInfo)
            }

            override fun onServiceLost(serviceInfo: NsdServiceInfo) = lost(serviceInfo.serviceName)

            override fun onStartDiscoveryFailed(
                serviceType: String,
                errorCode: Int,
            ) {
                Log.w("Edgepad", "Browsing for laptops failed to start: $errorCode")
                synchronized(this@Discovery) { listener = null }
            }

            override fun onStopDiscoveryFailed(
                serviceType: String,
                errorCode: Int,
            ) = Unit

            override fun onDiscoveryStarted(serviceType: String) = Unit

            override fun onDiscoveryStopped(serviceType: String) = Unit
        }

    @Synchronized
    private fun resolve(info: NsdServiceInfo) {
        if (listener == null) return
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            watch(info)
        } else {
            queue.addLast(info)
            resolveNext()
        }
    }

    @TargetApi(Build.VERSION_CODES.UPSIDE_DOWN_CAKE)
    private fun watch(info: NsdServiceInfo) {
        val name = info.serviceName
        if (name in watching) return
        val callback =
            object : NsdManager.ServiceInfoCallback {
                override fun onServiceInfoCallbackRegistrationFailed(errorCode: Int) = Unit

                override fun onServiceUpdated(serviceInfo: NsdServiceInfo) =
                    update(serviceInfo, serviceInfo.hostAddresses)

                override fun onServiceLost() = lost(name)

                override fun onServiceInfoCallbackUnregistered() = Unit
            }
        val manager = nsd ?: return
        try {
            manager.registerServiceInfoCallback(info, direct, callback)
            watching[name] = { manager.unregisterServiceInfoCallback(callback) }
        } catch (e: IllegalArgumentException) {
            Log.w("Edgepad", "Could not watch $name", e)
        }
    }

    @Suppress("DEPRECATION")
    private fun resolveNext() {
        if (resolving) return
        val manager = nsd ?: return
        val info = queue.removeFirstOrNull() ?: return
        resolving = true
        val done = {
            synchronized(this) {
                resolving = false
                resolveNext()
            }
        }
        manager.resolveService(
            info,
            object : NsdManager.ResolveListener {
                override fun onResolveFailed(
                    serviceInfo: NsdServiceInfo,
                    errorCode: Int,
                ) = done()

                override fun onServiceResolved(serviceInfo: NsdServiceInfo) {
                    update(serviceInfo, listOfNotNull(serviceInfo.host))
                    done()
                }
            },
        )
    }

    private fun update(
        info: NsdServiceInfo,
        hosts: List<InetAddress>,
    ) {
        val txt = parseTxt(info.attributes) ?: return
        if (hosts.isEmpty() || info.port !in 1..MAX_PORT) return
        val snapshot =
            synchronized(this) {
                if (listener == null) return
                services[info.serviceName] =
                    Found(txt.laptopId, txt.name ?: info.serviceName, hosts, info.port, Transport.WIFI, txt.version)
                services.values.toList()
            }
        onChange(snapshot)
    }

    private fun lost(name: String) {
        val snapshot =
            synchronized(this) {
                if (services.remove(name) == null) return
                services.values.toList()
            }
        onChange(snapshot)
    }

    private companion object {
        const val SERVICE_TYPE = "_edgepad._tcp"
        const val MAX_PORT = 65535
    }
}
