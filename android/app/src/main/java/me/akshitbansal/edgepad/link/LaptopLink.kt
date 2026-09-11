package me.akshitbansal.edgepad.link

import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothSocket
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.FrameCodec
import me.akshitbansal.edgepad.protocol.ProtocolConstants
import java.io.DataInputStream
import java.io.EOFException
import java.io.IOException
import java.io.OutputStream
import java.io.StreamCorruptedException
import java.util.concurrent.LinkedBlockingQueue
import kotlin.concurrent.thread

/**
 * One RFCOMM connection to the laptop.
 *
 * [open] blocks for the life of the connection, so callers run it on their own thread; it reads on that
 * thread. Frames go out through one writer thread, so a slow link can only ever block that thread —
 * never the UI and never touch handling. [send] never blocks. The [listener] can be swapped, so the link
 * survives the activity being rebuilt for a rotation or a theme change.
 */
class LaptopLink(
    private val device: BluetoothDevice,
    @Volatile var listener: Listener,
) {
    interface Listener {
        fun onConnected(link: LaptopLink)

        fun onFrame(frame: Frame)

        fun onClosed(
            link: LaptopLink,
            reason: String,
        )
    }

    private val outbox = LinkedBlockingQueue<Frame>()
    private val lock = Any()

    @Volatile private var socket: BluetoothSocket? = null

    @Volatile private var writer: Thread? = null

    @Volatile private var closed = false

    @Volatile private var handshakeDone = false

    /** True while the laptop has accepted this phone and the link is open. */
    val connected: Boolean get() = handshakeDone && !closed

    /** True once the laptop accepted this phone, even after the link closed; false means refused or unreachable. */
    val wasConnected: Boolean get() = handshakeDone

    fun send(frame: Frame) {
        if (!closed) outbox.put(frame)
    }

    fun open() {
        try {
            val s = device.createRfcommSocketToServiceRecord(ProtocolConstants.SERVICE_ID)
            socket = s
            s.connect()
            val input = DataInputStream(s.inputStream)
            val output = s.outputStream
            output.write(FrameCodec.encode(Frame.Hello(ProtocolConstants.VERSION)))
            output.flush()
            val ack = readFrame(input)
            if (ack is Frame.HelloAck &&
                ack.version != ProtocolConstants.VERSION
            ) {
                throw IOException(mismatch(ack.version))
            }
            if (ack != Frame.HelloAck(ProtocolConstants.VERSION)) throw IOException("The laptop answered with $ack")
            handshakeDone = true
            listener.onConnected(this)
            val w = thread(name = "edgepad-writer", priority = Thread.MAX_PRIORITY) { writeLoop(output) }
            writer = w
            // A close() that raced the line above found no writer to interrupt.
            if (closed) w.interrupt()
            while (!closed) listener.onFrame(readFrame(input))
        } catch (e: EOFException) {
            // The laptop hangs up before HELLO_ACK when it trusts a different phone.
            close(if (handshakeDone) e.message ?: "Connection lost" else REFUSED)
        } catch (e: IOException) {
            close(e.message ?: "Connection lost")
        } catch (e: SecurityException) {
            close("The Nearby devices permission was revoked")
        }
    }

    fun close(reason: String) {
        synchronized(lock) {
            if (closed) return
            closed = true
        }
        try {
            socket?.close()
        } catch (e: IOException) {
            // Already broken; closing is all that was asked for.
        }
        writer?.interrupt()
        listener.onClosed(this, reason)
    }

    private fun writeLoop(output: OutputStream) {
        val buffer = ByteArray(FrameCodec.MAX_FRAME_LENGTH * BATCH_FRAMES)
        val pending = ArrayList<Frame>(BATCH_FRAMES)
        try {
            while (!closed) {
                pending.clear()
                pending.add(outbox.take())
                // Whatever queued up behind the first frame goes out in the same write: one packet, not many,
                // and a backlog of moves collapses into one (see Coalesce) instead of replaying late.
                while (pending.size < BATCH_FRAMES) {
                    pending.add(outbox.poll() ?: break)
                }
                var length = 0
                for (frame in Coalesce.merge(pending)) {
                    length += FrameCodec.encode(frame, buffer, length)
                }
                output.write(buffer, 0, length)
                output.flush()
            }
        } catch (e: IOException) {
            close(e.message ?: "Connection lost")
        } catch (e: InterruptedException) {
            // close() interrupts the writer; nothing left to do.
        }
    }

    private fun readFrame(input: DataInputStream): Frame {
        val type = input.read()
        if (type < 0) throw EOFException("The laptop closed the connection")
        val length = FrameCodec.payloadLength(type)
        if (length == FrameCodec.LENGTH_PREFIXED) {
            val header = ByteArray(FrameCodec.TEXT_HEADER_LENGTH)
            input.readFully(header)
            val body = ByteArray(header[1].toInt() and 0xFF)
            input.readFully(body)
            return FrameCodec.decode(type, header + body)
        }
        if (length < 0) throw StreamCorruptedException("Unknown frame type 0x%02x".format(type))
        val payload = ByteArray(length)
        input.readFully(payload)
        return FrameCodec.decode(type, payload)
    }

    companion object {
        const val REFUSED =
            "The laptop hung up before the handshake. Its Edgepad may be older than this app: quit it from the " +
                "tray and run the latest. Or it trusts another phone: use Forget in its tray menu."

        /** A laptop on another protocol version answers with its own, then hangs up. */
        fun mismatch(laptop: Int): String =
            "The laptop runs a different Edgepad release (protocol $laptop, this app ${ProtocolConstants.VERSION}). " +
                "Install both from the same release."

        private const val BATCH_FRAMES = 32
    }
}
