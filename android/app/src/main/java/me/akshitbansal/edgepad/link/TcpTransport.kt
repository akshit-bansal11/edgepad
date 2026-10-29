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
 * one after another, the likeliest first ([orderHosts]), each for at most [CONNECT_TIMEOUT_MS].
 *
 * Once the socket is up every failure is a [LinkFailure]: the laptop was reached, so it is never
 * [LaptopLink.Cause.UNREACHABLE]. A laptop that no longer knows this phone, or answers a resume with the wrong key,
 * costs the stored key, so the next tap pairs afresh with a code the owner types on the laptop.
 */
class TcpTransport(
    private val hosts: List<InetAddress>,
    private val port: Int,
    private val laptopId: ByteArray,
    private val keys: PairingKeys,
    private val phoneName: String,
    private val initiator: LinkInitiator = LinkInitiator(),
    private val ownAddresses: () -> List<InetAddress> = ::localAddresses,
) : LinkTransport {
    @Volatile private var socket: Socket? = null

    @Volatile private var closed = false

    override fun open(showCode: (Sas) -> Unit): Streams {
        val s = connect()
        try {
            return handshake(s, showCode)
        } catch (e: IOException) {
            close()
            throw e as? LinkFailure ?: LinkFailure(LaptopLink.Closure(LaptopLink.Cause.REFUSED))
        } catch (e: GeneralSecurityException) {
            // The laptop sent a key that is not a point on the curve: nothing it says can be trusted.
            close()
            throw LinkFailure(LaptopLink.Closure(LaptopLink.Cause.PAIRING_FAILED))
        }
    }

    override fun close() {
        closed = true
        try {
            socket?.close()
        } catch (e: IOException) {
            // Already broken; closing is all that was asked for.
        }
    }

    private fun connect(): Socket {
        var last = IOException("The laptop has no address to connect to")
        for (host in orderHosts(hosts, ownAddresses())) {
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
                if (closed) throw e
                last = e
            }
        }
        throw last
    }

    private fun handshake(
        s: Socket,
        showCode: (Sas) -> Unit,
    ): Streams {
        val input = s.getInputStream()
        val output = s.getOutputStream()
        val key = keys.pairingKey(laptopId)
        val result =
            if (key != null) {
                try {
                    initiator.resume(input, output, keys.phoneId, laptopId, key)
                } finally {
                    key.fill(0)
                }
            } else {
                initiator.pair(input, output, keys.phoneId, phoneName) { sas ->
                    // The owner walks to the laptop and types; the phone's own limit on that is the screen's.
                    s.soTimeout = PAIRING_TIMEOUT_MS
                    showCode(sas)
                }
            }
        val refused = closureForStatus(result.status)
        if (refused != null) {
            if (refused.cause == LaptopLink.Cause.NOT_PAIRED) keys.forget(laptopId)
            throw LinkFailure(refused)
        }
        val channel = result.channel
        if (channel == null) {
            // A pairing nobody finished on the laptop, or a resume the laptop could not confirm.
            if (key != null) keys.forget(laptopId)
            val cause = if (key != null) LaptopLink.Cause.NOT_PAIRED else LaptopLink.Cause.PAIRING_FAILED
            throw LinkFailure(LaptopLink.Closure(cause))
        }
        if (key == null) {
            // Kept under the id the laptop proved, which is the one its later resumes will check.
            keys.save(result.laptopId, result.pairingKey)
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
