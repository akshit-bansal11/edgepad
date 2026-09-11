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
 * never the UI and never touch handling. [send] never blocks.
 */
class LaptopLink(
    private val device: BluetoothDevice,
    private val listener: Listener,
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
            if (ack != Frame.HelloAck(ProtocolConstants.VERSION)) throw IOException("The laptop answered with $ack")
            listener.onConnected(this)
            writer = thread(name = "edgepad-writer", priority = Thread.MAX_PRIORITY) { writeLoop(output) }
            while (!closed) listener.onFrame(readFrame(input))
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
        try {
            while (!closed) {
                var length = FrameCodec.encode(outbox.take(), buffer, 0)
                // Whatever queued up behind the first frame goes out in the same write: one packet, not many.
                while (length + FrameCodec.MAX_FRAME_LENGTH <= buffer.size) {
                    val next = outbox.poll() ?: break
                    length += FrameCodec.encode(next, buffer, length)
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
        if (length < 0) throw StreamCorruptedException("Unknown frame type 0x%02x".format(type))
        val payload = ByteArray(length)
        input.readFully(payload)
        return FrameCodec.decode(type, payload)
    }

    private companion object {
        const val BATCH_FRAMES = 32
    }
}
