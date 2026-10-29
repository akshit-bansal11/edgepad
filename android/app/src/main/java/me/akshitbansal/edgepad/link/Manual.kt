package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.net.LinkInitiator
import me.akshitbansal.edgepad.link.net.LinkWire
import java.io.IOException
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.net.UnknownHostException

/** An address typed by hand. [host] is a name or an IP literal, without the brackets an IPv6 literal was typed in. */
class HostPort(
    val host: String,
    val port: Int,
)

/**
 * `host`, `host:port`, `1.2.3.4:port`, `[::1]:port`, or a bare IPv6 literal (which cannot carry a port, its
 * colons being its own); [LINK_PORT] when no port is given. Null for anything else. Pure, so a test covers it.
 */
internal fun parseHostPort(text: String): HostPort? {
    val t = text.trim()
    if (t.isEmpty() || t.any { !it.isLetterOrDigit() && it !in ADDRESS_CHARS }) return null
    if (t.startsWith("[")) {
        val end = t.indexOf(']')
        val host = if (end > 0) t.substring(1, end) else ""
        if (':' !in host) return null
        val rest = t.substring(end + 1)
        val port =
            when {
                rest.isEmpty() -> LINK_PORT
                rest.startsWith(":") -> portOf(rest.substring(1))
                else -> null
            } ?: return null
        return HostPort(host, port)
    }
    return when (t.count { it == ':' }) {
        0 -> {
            HostPort(t, LINK_PORT)
        }

        1 -> {
            val host = t.substringBefore(':')
            val port = portOf(t.substringAfter(':'))
            if (host.isEmpty() || port == null) null else HostPort(host, port)
        }

        else -> {
            HostPort(t, LINK_PORT)
        }
    }
}

private const val ADDRESS_CHARS = ":.-_[]%"

private fun portOf(text: String): Int? {
    if (text.isEmpty() || text.any { it !in '0'..'9' }) return null
    return text.toIntOrNull()?.takeIf { it in 1..MAX_PORT }
}

private const val MAX_PORT = 65535

/**
 * Connects to [address] and asks INFO, which is what tells the phone the laptop's id and name; null when nothing
 * there speaks Edgepad's link. Blocking: run it off the main thread.
 */
internal fun probeAddress(
    address: HostPort,
    initiator: LinkInitiator = LinkInitiator(),
): Found? {
    val hosts =
        try {
            InetAddress.getAllByName(address.host).toList()
        } catch (e: UnknownHostException) {
            return null
        }
    for (host in hosts) {
        try {
            Socket().use { s ->
                s.connect(InetSocketAddress(host, address.port), CONNECT_MS)
                s.soTimeout = REPLY_MS
                val reply = initiator.info(s.getInputStream(), s.getOutputStream())
                if (reply.status != LinkWire.STATUS_OK || reply.laptopId.size != LinkWire.ID_LENGTH) return@use
                val name = reply.name.ifBlank { address.host }
                return Found(reply.laptopId, name, hosts, address.port, Transport.WIFI, reply.protocolVersion)
            }
        } catch (e: IOException) {
            // This address did not answer; the next may.
        }
    }
    return null
}

private const val CONNECT_MS = 3_000
private const val REPLY_MS = 5_000
