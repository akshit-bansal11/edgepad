package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.net.LinkInitiator
import me.akshitbansal.edgepad.link.net.LinkWire
import me.akshitbansal.edgepad.link.net.PairingKeys
import me.akshitbansal.edgepad.link.net.Sas
import java.io.IOException
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.Socket
import java.net.SocketException
import java.security.GeneralSecurityException

/**
 * A TCP connection to a laptop on the same Wi-Fi or over USB tethering, with EDGL1 in front of the frames: a
 * resume when [keys] holds a pairing key for [laptopId], a pairing otherwise. The laptop's addresses are tried
 * one after another, the likeliest first ([orderHosts]), each for at most [CONNECT_TIMEOUT_MS]; one that answers
 * a resume but cannot prove it is the laptop ([LaptopLink.Cause.NOT_THIS_LAPTOP]) is passed over for the next.
 *
 * Once the socket is up every failure is a [LinkFailure]: the laptop was reached, so it is never
 * [LaptopLink.Cause.UNREACHABLE]. A stored key is never dropped on anything the network says, since a status or a
 * failed confirmation is no proof: a laptop that no longer knows this phone closes as
 * [LaptopLink.Cause.NOT_PAIRED], and the key is replaced only by a new pairing, [forcePair], that the owner
 * confirmed on the phone.
 */
class TcpTransport(
    private val hosts: List<InetAddress>,
    private val port: Int,
    private val laptopId: ByteArray,
    private val keys: PairingKeys,
    private val phoneName: String,
    private val forcePair: Boolean = false,
    private val initiator: LinkInitiator = LinkInitiator(),
    private val ownAddresses: () -> List<InetAddress> = ::localAddresses,
) : LinkTransport {
    @Volatile private var socket: Socket? = null

    @Volatile private var closed = false

    override fun open(
        showCode: (Sas) -> Unit,
        confirmed: () -> Boolean,
    ): Streams {
        var last = IOException("The laptop has no address to connect to")
        for (host in orderHosts(hosts, ownAddresses())) {
            val s =
                try {
                    connect(host)
                } catch (e: IOException) {
                    if (closed) throw e
                    last = e
                    continue
                }
            try {
                return handshake(s, showCode, confirmed)
            } catch (e: LinkFailure) {
                // Something else on the network may answer at one of the laptop's addresses: one that cannot
                // prove it is the laptop ends the try at that address only, never the attempt.
                closeQuietly(s)
                if (closed || e.closure.cause != LaptopLink.Cause.NOT_THIS_LAPTOP) {
                    close()
                    throw e
                }
                last = e
            } catch (e: IOException) {
                close()
                throw LinkFailure(LaptopLink.Closure(LaptopLink.Cause.REFUSED))
            } catch (e: GeneralSecurityException) {
                // The laptop sent a key that is not a point on the curve: nothing it says can be trusted.
                close()
                throw LinkFailure(LaptopLink.Closure(LaptopLink.Cause.PAIRING_FAILED))
            }
        }
        throw last
    }

    override fun close() {
        closed = true
        socket?.let(::closeQuietly)
    }

    private fun closeQuietly(s: Socket) {
        try {
            s.close()
        } catch (e: IOException) {
            // Already broken; closing is all that was asked for.
        }
    }

    private fun connect(host: InetAddress): Socket {
        val s = Socket()
        socket = s
        // A close() that ran before the line above found no socket to close.
        if (closed) {
            s.close()
            throw IOException("Closed before connecting")
        }
        try {
            s.connect(InetSocketAddress(host, port), CONNECT_TIMEOUT_MS)
            s.tcpNoDelay = true
            s.keepAlive = true
            s.soTimeout = MESSAGE_TIMEOUT_MS
            return s
        } catch (e: IOException) {
            s.close()
            throw e
        }
    }

    private fun handshake(
        s: Socket,
        showCode: (Sas) -> Unit,
        confirmed: () -> Boolean,
    ): Streams {
        val input = s.getInputStream()
        val output = s.getOutputStream()
        val key = if (forcePair) null else keys.pairingKey(laptopId)
        val result =
            if (key != null) {
                try {
                    initiator.resume(input, output, keys.phoneId, laptopId, key)
                } finally {
                    key.fill(0)
                }
            } else {
                initiator.pair(input, output, keys.phoneId, phoneName, laptopId, confirmed) { sas ->
                    // The owner walks to the laptop and types; the phone's own limit on that is the screen's.
                    s.soTimeout = PAIRING_TIMEOUT_MS
                    showCode(sas)
                }
            }
        closureForStatus(result.status)?.let { throw LinkFailure(it) }
        // A resume whose answer could not prove it came from the laptop, or a pairing another laptop answered,
        // nobody finished on the laptop, or the owner said was not accepted. Nothing is stored or forgotten either
        // way, and a failed proof is no reason to pair again: whatever answered may not be the laptop at all.
        val channel = result.channel
        if (channel == null) {
            val cause = if (key != null) LaptopLink.Cause.NOT_THIS_LAPTOP else LaptopLink.Cause.PAIRING_FAILED
            throw LinkFailure(LaptopLink.Closure(cause))
        }
        if (key == null) {
            // The owner said yes, and the laptop proved it is the one asked for: only now is a key kept or replaced.
            keys.save(laptopId, result.pairingKey)
            result.pairingKey.fill(0)
        }
        s.soTimeout = 0
        return Streams(channel.input, channel.output)
    }

    private companion object {
        const val CONNECT_TIMEOUT_MS = 3_000

        /** The longest wait for one handshake message, as the laptop allows. */
        const val MESSAGE_TIMEOUT_MS = 10_000

        /** The pairing screen's countdown: the laptop gives its owner 60 s, the phone a little more. */
        const val PAIRING_TIMEOUT_MS = 90_000
    }
}

/**
 * What a handshake reply's status closes the link with, or null to carry on. Pure, so a test covers it without
 * a socket.
 */
internal fun closureForStatus(status: Int): LaptopLink.Closure? =
    when (status) {
        LinkWire.STATUS_OK -> null
        LinkWire.STATUS_BUSY -> LaptopLink.Closure(LaptopLink.Cause.PAIRING_LOCKED)
        LinkWire.STATUS_UNKNOWN_PHONE -> LaptopLink.Closure(LaptopLink.Cause.NOT_PAIRED)
        else -> LaptopLink.Closure(LaptopLink.Cause.UNEXPECTED, "link status $status")
    }

/**
 * The laptop's addresses in the order to try them: those on the same /24 as one of this phone's IPv4 addresses
 * first, then other IPv4, then IPv6, each group in the order given. A laptop on two networks is reached on the one
 * the phone shares with it rather than waiting out a timeout on the other.
 */
internal fun orderHosts(
    hosts: List<InetAddress>,
    own: List<InetAddress>,
): List<InetAddress> {
    val networks = own.filterIsInstance<Inet4Address>().map { it.address.copyOf(3).toList() }.toSet()
    return hosts.sortedBy {
        when {
            it is Inet4Address && it.address.copyOf(3).toList() in networks -> 0
            it is Inet4Address -> 1
            else -> 2
        }
    }
}

private fun localAddresses(): List<InetAddress> =
    try {
        NetworkInterface
            .getNetworkInterfaces()
            ?.toList()
            .orEmpty()
            .filter { it.isUp }
            .flatMap { it.inetAddresses.toList() }
    } catch (e: SocketException) {
        emptyList()
    }
