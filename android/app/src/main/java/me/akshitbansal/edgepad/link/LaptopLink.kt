package me.akshitbansal.edgepad.link

import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothSocket
import android.os.Handler
import android.os.Looper
import android.util.Log
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
 *
 * Every [Listener] call is posted to the main thread, and [listener] is read when the call runs rather
 * than when it was posted. That is what lets the link outlive the activity across a rotation or a theme
 * change: the old activity is destroyed and the new one sets itself as the listener inside one main-thread
 * message, so anything the link reports in between lands on the new activity instead of the dead one.
 */
class LaptopLink(
    private val device: BluetoothDevice,
    @Volatile var listener: Listener,
) {
    interface Listener {
        fun onConnected(link: LaptopLink)

        /** The laptop has had HELLO for a while and not answered; it may be asking its owner about this phone. */
        fun onAwaitingLaptop(link: LaptopLink)

        fun onFrame(frame: Frame)

        /** A PONG's round trip, measured on the reading thread so the main thread's queue is not in it. */
        fun onRoundTrip(ms: Double)

        fun onClosed(
            link: LaptopLink,
            closure: Closure,
        )
    }

    /**
     * Why a link closed. [ENDED] is the only one this phone chose, which is what the activity tells apart:
     * it was compared against a localized string until 3.2, so a translation could have turned every
     * deliberate disconnect into a dropped link.
     */
    enum class Cause {
        /** This phone closed it: a disconnect, a forget, the app leaving the foreground. */
        ENDED,

        /** The socket failed or the laptop hung up after accepting this phone. */
        LOST,

        /** The laptop hung up before its HELLO_ACK: it trusts another phone, or it said no to this one. */
        REFUSED,

        /** The laptop runs another protocol version, which [Closure.detail] carries. */
        MISMATCH,

        /** The laptop answered HELLO with something that is not a HELLO_ACK. */
        UNEXPECTED,

        /** Nearby devices was revoked while the link was up. */
        NO_PERMISSION,

        /** The laptop took the connection and never answered HELLO. */
        NO_ANSWER,
    }

    /**
     * A [Cause], and for LOST the platform's own message (in whatever language the platform wrote it), for
     * MISMATCH the laptop's protocol version, for UNEXPECTED the frame it sent. Empty otherwise. The words
     * the user reads are the activity's, from strings.xml; nothing here is shown as it stands.
     */
    class Closure(
        val cause: Cause,
        val detail: String = "",
    )

    /**
     * Bounded, where it used to grow without limit. RFCOMM only blocks the writer when the laptop stops
     * reading, and a queue this deep behind a blocked write is a link that has already failed: see [send].
     */
    private val outbox = LinkedBlockingQueue<Frame>(OUTBOX_CAPACITY)
    private val lock = Any()
    private val main = Handler(Looper.getMainLooper())

    @Volatile private var socket: BluetoothSocket? = null

    @Volatile private var writer: Thread? = null

    @Volatile private var closed = false

    @Volatile private var handshakeDone = false

    private val slowAnswer = Runnable { if (!handshakeDone && !closed) deliver { it.onAwaitingLaptop(this) } }
    private val noAnswer = Runnable { if (!handshakeDone) close(Closure(Cause.NO_ANSWER)) }

    /** True while the laptop has accepted this phone and the link is open. */
    val connected: Boolean get() = handshakeDone && !closed

    /** True once the laptop accepted this phone, even after the link closed; false means refused or unreachable. */
    val wasConnected: Boolean get() = handshakeDone

    /**
     * Queues [frame] for the writer. Dropped until the laptop has accepted this phone: input from before
     * the handshake describes fingers that have long since moved, and replaying it the moment the link opens
     * would move the pointer on its own. A full outbox closes the link as lost, rather than dropping the
     * frame, because the frame could be a key's release and losing that would leave the key held down.
     */
    fun send(frame: Frame) {
        if (!connected) return
        if (!outbox.offer(frame)) close(Closure(Cause.LOST))
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
            // The laptop may hold its answer while its owner decides whether to trust this phone, so the
            // wait for HELLO_ACK has an end of its own: longer than the laptop's own prompt, after which it
            // answers no by itself, so that the laptop's no is what the user sees rather than this timeout.
            main.postDelayed(slowAnswer, SLOW_ANSWER_MS)
            main.postDelayed(noAnswer, ANSWER_TIMEOUT_MS)
            val ack = readFrame(input)
            main.removeCallbacks(slowAnswer)
            main.removeCallbacks(noAnswer)
            if (ack is Frame.HelloAck && ack.version != ProtocolConstants.VERSION) {
                close(Closure(Cause.MISMATCH, ack.version.toString()))
                return
            }
            if (ack != Frame.HelloAck(ProtocolConstants.VERSION)) {
                close(Closure(Cause.UNEXPECTED, ack.toString()))
                return
            }
            handshakeDone = true
            deliver { it.onConnected(this) }
            val w = thread(name = "edgepad-writer", priority = Thread.MAX_PRIORITY) { writeLoop(output) }
            writer = w
            // A close() that raced the line above found no writer to interrupt.
            if (closed) w.interrupt()
            while (!closed) {
                val frame = readFrame(input)
                if (frame is Frame.Pong) {
                    val ms = (System.nanoTime() - frame.time) / NANOS_PER_MS
                    deliver { it.onRoundTrip(ms) }
                } else {
                    deliver { it.onFrame(frame) }
                }
            }
        } catch (e: EOFException) {
            // The laptop hangs up before HELLO_ACK when it trusts a different phone, or its owner said no.
            close(if (handshakeDone) Closure(Cause.LOST, e.message.orEmpty()) else Closure(Cause.REFUSED))
        } catch (e: IOException) {
            close(Closure(Cause.LOST, e.message.orEmpty()))
        } catch (e: SecurityException) {
            close(Closure(Cause.NO_PERMISSION))
        } catch (e: RuntimeException) {
            // A bug on this side, not a fault on the link. Before 3.2 it escaped the thread and took the whole
            // app down with it; ending the one link is the most it should cost.
            Log.e(TAG, "The link's reader failed", e)
            close(Closure(Cause.LOST, e.toString()))
        }
    }

    fun close(closure: Closure) {
        synchronized(lock) {
            if (closed) return
            closed = true
        }
        main.removeCallbacks(slowAnswer)
        main.removeCallbacks(noAnswer)
        try {
            socket?.close()
        } catch (e: IOException) {
            // Already broken; closing is all that was asked for.
        }
        writer?.interrupt()
        outbox.clear()
        deliver { it.onClosed(this, closure) }
    }

    private fun deliver(call: (Listener) -> Unit) {
        main.post { call(listener) }
    }

    private fun writeLoop(output: OutputStream) {
        val buffer = ByteArray(FrameCodec.MAX_FRAME_LENGTH * BATCH_FRAMES)
        val pending = ArrayList<Frame>()
        try {
            while (!closed) {
                pending.clear()
                pending.add(outbox.take())
                // Everything queued behind the first frame goes out now, the whole backlog and not just the
                // next few frames of it, so a run of moves really does collapse into one (see Coalesce)
                // instead of replaying late. The buffer is written whenever it fills, so a long backlog is
                // several writes, never a bigger buffer.
                outbox.drainTo(pending)
                var length = 0
                for (frame in Coalesce.merge(pending)) {
                    if (length + FrameCodec.MAX_FRAME_LENGTH > buffer.size) {
                        output.write(buffer, 0, length)
                        length = 0
                    }
                    length += encode(frame, buffer, length)
                }
                output.write(buffer, 0, length)
                output.flush()
            }
        } catch (e: IOException) {
            close(Closure(Cause.LOST, e.message.orEmpty()))
        } catch (e: InterruptedException) {
            // close() interrupts the writer; nothing left to do.
        }
    }

    /**
     * One frame into [buffer], or nothing when the codec refuses it. A field out of range is a bug in
     * whatever built the frame; dropping that frame costs one input, while letting the exception out
     * would end the writer thread and, before 3.2, the app.
     */
    private fun encode(
        frame: Frame,
        buffer: ByteArray,
        offset: Int,
    ): Int =
        try {
            FrameCodec.encode(frame, buffer, offset)
        } catch (e: IllegalArgumentException) {
            Log.e(TAG, "Dropped a frame the codec refused: $frame", e)
            0
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

    private companion object {
        const val TAG = "Edgepad"
        const val BATCH_FRAMES = 32

        /**
         * Half a minute or so of a finger moving at a touchscreen's report rate without one write getting
         * through, which no working link comes near and no dead one needs to be waited out for.
         */
        const val OUTBOX_CAPACITY = 4096

        /** How long the laptop may take over HELLO before the phone says it may be asking its owner. */
        const val SLOW_ANSWER_MS = 3_000L

        /** The laptop's trust prompt gives up after 60 s; this waits longer, so the laptop's answer arrives first. */
        const val ANSWER_TIMEOUT_MS = 90_000L
        const val NANOS_PER_MS = 1_000_000.0
    }
}
