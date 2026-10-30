package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.net.LinkInitiator
import me.akshitbansal.edgepad.link.net.LinkWire
import java.io.IOException
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.Socket
import java.net.SocketException
import java.util.concurrent.Callable
import java.util.concurrent.ExecutionException
import java.util.concurrent.Executors
import java.util.concurrent.Future

/** The port a laptop listens on by default; docs/PROTOCOL.md has it once for both apps. */
const val LINK_PORT = 47321

/**
 * What kind of link the interface called [name] is, or null for one that is neither: USB tethering (`rndis`, `usb`,
 * `ncm`) or an Ethernet-class link (`eth`) is [Transport.USB]; Wi-Fi (`wlan`, `wifi`) or this phone's own hotspot
 * (`ap`, `swlan`) is [Transport.WIFI]. A name is a guess at what the interface is, never proof.
 */
internal fun transportOfInterface(name: String): Transport? =
    when {
        USB_PREFIXES.any { name.startsWith(it) } -> Transport.USB
        WIFI_PREFIXES.any { name.startsWith(it) } -> Transport.WIFI
        else -> null
    }

private val USB_PREFIXES = listOf("rndis", "usb", "ncm", "eth")
private val WIFI_PREFIXES = listOf("wlan", "wifi", "ap", "swlan")

// ponytail: a /24 every rescan while mDNS is empty; back off or probe only the router's neighbours if it costs.
/**
 * Whether a subnet of [transport] is swept. USB always is. Wi-Fi only while mDNS has found nothing: multicast is
 * unreliable on many routers, which is what the sweep is for, and a repeated sweep of a /24 is not free.
 */
internal fun shouldSweep(
    transport: Transport,
    mdnsFound: Boolean,
): Boolean = transport == Transport.USB || !mdnsFound

/** What one pass saw: the laptops found, and the address of the Wi-Fi and USB interface seen (null when none). */
class Sweep(
    val found: List<Found>,
    val wifi: String?,
    val usb: String?,
)

/**
 * The other addresses on the subnet of [address]/[prefix], to try the laptop's port on. A subnet wider than a /24
 * is cut to the /24 around the phone, so a /16 is 253 hosts and not 65,533; the network and broadcast addresses
 * and the phone itself are left out.
 */
internal fun hostsOf(
    address: InetAddress,
    prefix: Int,
): List<InetAddress> {
    val bytes = address.address
    if (bytes.size != 4) return emptyList()
    val bits = prefix.coerceIn(MIN_PREFIX, 32)
    val own = bytes.fold(0L) { acc, b -> (acc shl 8) or (b.toLong() and 0xff) }
    val size = 1L shl (32 - bits)
    val network = own - own % size
    val firstHost = if (bits <= 30) network + 1 else network
    val lastHost = if (bits <= 30) network + size - 2 else network + size - 1
    return (firstHost..lastHost).filter { it != own }.map { value ->
        InetAddress.getByAddress(ByteArray(4) { (value shr (24 - 8 * it)).toByte() })
    }
}

private const val MIN_PREFIX = 24

/**
 * Looks for laptops on the Wi-Fi and tether-like interfaces by connecting to [LINK_PORT] on every host of their subnet, 32
 * at a time, 250 ms each, and asking INFO of whoever answers. Blocking: run it on its own thread, and [stop] from
 * any other to abandon it. Only a reply that is a well-formed EDGL1 INFO counts, so some other service on the port
 * is not listed.
 */
class TetherProbe(
    private val initiator: LinkInitiator = LinkInitiator(),
) {
    @Volatile private var stopped = false

    fun stop() {
        stopped = true
    }

    /** [mdnsFound] is whether mDNS has already found a laptop, which spares the Wi-Fi sweep; see [shouldSweep]. */
    fun run(
        port: Int = LINK_PORT,
        mdnsFound: Boolean = false,
    ): Sweep {
        stopped = false
        val nets = interfaces()
        val wifi = nets.firstOrNull { it.transport == Transport.WIFI }?.address?.hostAddress
        val usb = nets.firstOrNull { it.transport == Transport.USB }?.address?.hostAddress
        val targets =
            nets
                .filter { shouldSweep(it.transport, mdnsFound) }
                .flatMap { net -> hostsOf(net.address, net.prefix).map { it to net.transport } }
                .distinctBy { it.first }
        if (targets.isEmpty()) return Sweep(emptyList(), wifi, usb)
        val pool = Executors.newFixedThreadPool(IN_FLIGHT)
        try {
            val results: List<Future<Found?>> =
                targets.map { (host, transport) -> pool.submit(Callable { ask(host, transport, port) }) }
            val found =
                mergeFound(
                    results.mapNotNull {
                        try {
                            it.get()
                        } catch (e: ExecutionException) {
                            null
                        }
                    },
                )
            return Sweep(found, wifi, usb)
        } finally {
            pool.shutdownNow()
        }
    }

    private class Net(
        val transport: Transport,
        val address: InetAddress,
        val prefix: Int,
    )

    private fun interfaces(): List<Net> =
        try {
            NetworkInterface
                .getNetworkInterfaces()
                ?.toList()
                .orEmpty()
                .filter { it.isUp && !it.isLoopback }
                .flatMap { net ->
                    val transport = transportOfInterface(net.name)
                    if (transport == null) {
                        emptyList()
                    } else {
                        net.interfaceAddresses
                            .filter { it.address is Inet4Address && it.address.isSiteLocalAddress }
                            .map { Net(transport, it.address, it.networkPrefixLength.toInt()) }
                    }
                }
        } catch (e: SocketException) {
            emptyList()
        }

    private fun ask(
        host: InetAddress,
        transport: Transport,
        port: Int,
    ): Found? {
        if (stopped) return null
        try {
            Socket().use { s ->
                s.connect(InetSocketAddress(host, port), CONNECT_MS)
                s.soTimeout = REPLY_MS
                val reply = initiator.info(s.getInputStream(), s.getOutputStream())
                if (reply.status != LinkWire.STATUS_OK || reply.laptopId.size != LinkWire.ID_LENGTH) return null
                val name = reply.name.ifBlank { host.hostAddress.orEmpty() }
                return Found(reply.laptopId, name, listOf(host), port, transport, reply.protocolVersion)
            }
        } catch (e: IOException) {
            // Nothing there, or not Edgepad: most of a subnet is.
            return null
        }
    }

    private companion object {
        const val IN_FLIGHT = 32
        const val CONNECT_MS = 250
        const val REPLY_MS = 2_000
    }
}
