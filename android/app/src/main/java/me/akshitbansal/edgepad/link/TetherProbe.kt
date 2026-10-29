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
 * Whether [name] looks like a tethering interface: USB tethering (`rndis`, `usb`, `ncm`), or this phone's own
 * hotspot (`ap`, `swlan`), or an Ethernet-class link (`eth`). NsdManager is not assumed to see mDNS on these, so
 * they are probed for the laptop's port instead. A name is a guess at what the interface is, never proof.
 */
internal fun isTetherName(name: String): Boolean = TETHER_PREFIXES.any { name.startsWith(it) }

private val TETHER_PREFIXES = listOf("rndis", "usb", "ncm", "ap", "swlan", "eth")

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
 * Looks for laptops on the tether-like interfaces by connecting to [LINK_PORT] on every host of their subnet, 32
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

    fun run(port: Int = LINK_PORT): List<Found> {
        stopped = false
        val targets = tetherHosts()
        if (targets.isEmpty()) return emptyList()
        val pool = Executors.newFixedThreadPool(IN_FLIGHT)
        try {
            val results: List<Future<Found?>> = targets.map { host -> pool.submit(Callable { ask(host, port) }) }
            return mergeFound(
                results.mapNotNull {
                    try {
                        it.get()
                    } catch (e: ExecutionException) {
                        null
                    }
                },
            )
        } finally {
            pool.shutdownNow()
        }
    }

    private fun tetherHosts(): List<InetAddress> =
        try {
            NetworkInterface
                .getNetworkInterfaces()
                ?.toList()
                .orEmpty()
                .filter { it.isUp && !it.isLoopback && isTetherName(it.name) }
                .flatMap { net ->
                    net.interfaceAddresses
                        .filter { it.address is Inet4Address && it.address.isSiteLocalAddress }
                        .flatMap { hostsOf(it.address, it.networkPrefixLength.toInt()) }
                }.distinct()
        } catch (e: SocketException) {
            emptyList()
        }

    private fun ask(
        host: InetAddress,
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
                return Found(reply.laptopId, name, listOf(host), port, Transport.USB, reply.protocolVersion)
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
