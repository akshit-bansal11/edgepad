package me.akshitbansal.edgepad.link.xfer

import me.akshitbansal.edgepad.link.LINK_PORT
import me.akshitbansal.edgepad.link.Transport
import me.akshitbansal.edgepad.link.net.LinkInitiator
import me.akshitbansal.edgepad.link.net.LinkWire
import me.akshitbansal.edgepad.link.net.PairingKeys
import me.akshitbansal.edgepad.link.net.SecureChannel
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.FrameCodec
import me.akshitbansal.edgepad.protocol.ProtocolConstants
import java.io.Closeable
import java.io.DataInputStream
import java.io.IOException
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.security.GeneralSecurityException
import kotlin.concurrent.thread

/**
 * The second socket of a transfer (D-BULK): an EDGL1 resume to [host], the address the control link is connected to,
 * on [port], then BULK_HELLO as the first frame. It only ever attaches to a session the laptop already admitted, so
 * it asks for no trust and no PIN. It exists, with its reading thread, only between [open] and [close], so the phone
 * keeps neither while nothing is sent.
 *
 * [open] answers false, and the caller sends on the control link, on Bluetooth, on a missing pairing key, and on any
 * failure or refusal. A write that fails later closes the socket and hands that frame and every later one to the
 * fallback of [writer]; chunks carry their offset, so the laptop takes them from either stream.
 *
 * @param onFrame Takes what the laptop sends on this socket (any XFER frame), on the reading thread.
 */
class BulkSocket(
    private val host: InetAddress,
    private val transport: Transport,
    private val laptopId: ByteArray,
    private val keys: () -> PairingKeys,
    private val onFrame: (Frame) -> Unit,
    private val port: Int = LINK_PORT,
    private val initiator: LinkInitiator = LinkInitiator(),
) : Closeable {
    private val lock = Any()

    @Volatile private var socket: Socket? = null

    @Volatile private var channel: SecureChannel? = null

    /** Opens the socket and waits up to [ACK_TIMEOUT_MS] for HELLO_ACK. Blocks: never on the main thread. */
    fun open(): Boolean {
        if (transport == Transport.BLUETOOTH) return false
        val stored = keys()
        val key = stored.pairingKey(laptopId) ?: return false
        val s = Socket()
        try {
            socket = s
            s.connect(InetSocketAddress(host, port), CONNECT_TIMEOUT_MS)
            s.tcpNoDelay = true
            s.soTimeout = ACK_TIMEOUT_MS
            val input = s.getInputStream()
            val output = s.getOutputStream()
            val result =
                try {
                    initiator.resume(input, output, stored.phoneId, laptopId, key)
                } finally {
                    key.fill(0)
                }
            val opened = result.channel
            if (result.status != LinkWire.STATUS_OK || opened == null) return fail()
            opened.output.write(FrameCodec.encode(Frame.BulkHello(ProtocolConstants.VERSION)))
            opened.output.flush()
            val reader = DataInputStream(opened.input)
            if (readFrame(reader) !is Frame.HelloAck) return fail()
            s.soTimeout = 0
            channel = opened
            thread(name = "edgepad-bulk", priority = Thread.MIN_PRIORITY) { readLoop(reader) }
            return true
        } catch (e: IOException) {
            return fail()
        } catch (e: GeneralSecurityException) {
            return fail()
        }
    }

    /**
     * A writer that sends on this socket while it is up and on [fallback] otherwise, so a transfer finishes on the
     * control link when the socket was never opened or broke.
     */
    fun writer(fallback: (Frame) -> Unit): (Frame) -> Unit =
        { frame ->
            val out = synchronized(lock) { channel?.output }
            val sent =
                out != null &&
                    try {
                        synchronized(lock) {
                            out.write(FrameCodec.encode(frame))
                            out.flush()
                        }
                        true
                    } catch (e: IOException) {
                        close()
                        false
                    }
            if (!sent) fallback(frame)
        }

    /** Runs [block] at background priority on the calling thread, which gets its own priority back after. */
    fun <T> background(block: () -> T): T {
        val now = Thread.currentThread()
        val before = now.priority
        now.priority = Thread.MIN_PRIORITY
        try {
            return block()
        } finally {
            now.priority = before
        }
    }

    override fun close() {
        synchronized(lock) { channel = null }
        socket?.let {
            try {
                it.close()
            } catch (e: IOException) {
                // Already broken; closing is all that was asked for.
            }
        }
        socket = null
    }

    private fun fail(): Boolean {
        close()
        return false
    }

    private fun readLoop(input: DataInputStream) {
        try {
            while (true) onFrame(readFrame(input))
        } catch (e: IOException) {
            // The transfer's end, or a socket that broke: the next write falls back to the control link.
            close()
        }
    }

    private fun readFrame(input: DataInputStream): Frame =
        FrameCodec.read(input, "The laptop closed the transfer socket")

    private companion object {
        const val CONNECT_TIMEOUT_MS = 3_000
        const val ACK_TIMEOUT_MS = 10_000
    }
}
