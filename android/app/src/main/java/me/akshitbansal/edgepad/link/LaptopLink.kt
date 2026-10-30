package me.akshitbansal.edgepad.link

import android.os.Handler
import android.os.Looper
import android.util.Log
import me.akshitbansal.edgepad.link.net.Sas
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.FrameCodec
import me.akshitbansal.edgepad.protocol.Pin
import me.akshitbansal.edgepad.protocol.ProtocolConstants
import me.akshitbansal.edgepad.protocol.TextKind
import java.io.DataInputStream
import java.io.EOFException
import java.io.IOException
import java.io.OutputStream
import java.io.StreamCorruptedException
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import kotlin.concurrent.thread

/**
 * One connection to the laptop, over Bluetooth or the network: the [LinkTransport] opens the byte pipe and
 * everything from HELLO on is the same on both.
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
    private val transport: LinkTransport,
    @Volatile var listener: Listener,
) {
    interface Listener {
        fun onConnected(link: LaptopLink)

        /** The laptop has had HELLO for a while and not answered; it may be asking its owner about this phone. */
        fun onAwaitingLaptop(link: LaptopLink)

        /**
         * The laptop wants its PIN before it accepts this phone; answer with [answerPin]. [attemptsLeft] is 5 on
         * the first question and fewer after a wrong answer. A lock-out is never delivered here: it closes the
         * link as [Cause.PIN_LOCKED] instead.
         */
        fun onPinRequest(
            link: LaptopLink,
            attemptsLeft: Int,
        )

        /**
         * A network pairing: show [code] for the laptop's owner to type there. Closing the link is how the
         * pairing is cancelled.
         */
        fun onPairingCode(
            link: LaptopLink,
            code: Sas,
        )

        /**
         * The laptop answered the pairing. Ask the owner whether the laptop said the code matched, and answer with
         * [answerPairing]; no answer within [CONFIRM_TIMEOUT_MS] is a no, and so is closing the link.
         */
        fun onPairingConfirm(link: LaptopLink)

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

        /** The connection never opened: the laptop is off, out of range or not running Edgepad. */
        UNREACHABLE,

        /** The laptop took the connection, then hung up before its HELLO_ACK: its owner said no, or Edgepad is old. */
        REFUSED,

        /** The laptop runs another protocol version, which [Closure.detail] carries. */
        MISMATCH,

        /** The laptop answered HELLO with something that is not a HELLO_ACK. */
        UNEXPECTED,

        /** Nearby devices was revoked while the link was up. */
        NO_PERMISSION,

        /** The laptop took the connection and never answered HELLO. */
        NO_ANSWER,

        /** Too many wrong PINs: the laptop refuses this phone for the seconds [Closure.detail] carries. */
        PIN_LOCKED,

        /** The laptop said it has no pairing with this phone over the network; pair again. */
        NOT_PAIRED,

        /**
         * A device answered as the laptop but could not prove it holds the pairing key: maybe not the laptop at all,
         * so pairing again is not the answer. The key stays.
         */
        NOT_THIS_LAPTOP,

        /** A network pairing did not finish: the code was not typed on the laptop in time, or not correctly. */
        PAIRING_FAILED,

        /** The laptop is pairing another phone, or pauses pairing after too many wrong codes. */
        PAIRING_LOCKED,
    }

    /**
     * A [Cause], and for MISMATCH the laptop's protocol version, for UNEXPECTED the frame it sent, for
     * PIN_LOCKED the seconds the laptop will refuse this phone. Empty otherwise. The words the user reads are
     * the activity's, from strings.xml; nothing here is shown as it stands. The platform's own message for a
     * failed socket goes to the log instead: it is the same unhelpful text for every failure, but it still tells
     * a bug report which call gave up.
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

    /** The owner's one answer to [Listener.onPairingConfirm]; [close] puts a no in, so the wait for it ends. */
    private val pairingAnswer = LinkedBlockingQueue<Boolean>(1)
    private val lock = Any()

    /**
     * Held for the PIN answer's write. Not [lock]: a write blocked on a stalled link would then hold up
     * [close], which the main thread calls.
     */
    private val writeLock = Any()
    private val main = Handler(Looper.getMainLooper())

    /** The icon side, in pixels, asked of the laptop; raised by the activity as tiles grow, and 128 again on a new link. */
    @Volatile var iconPx: Int = 128

    @Volatile private var streams: Streams? = null

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
        // Whether connect() returned, which is what tells a laptop that never answered from one that hung up.
        var connectReturned = false
        try {
            val opened = transport.open({ code -> deliver { it.onPairingCode(this, code) } }, ::askOwner)
            streams = opened
            connectReturned = true
            val input = DataInputStream(opened.input)
            val output = opened.output
            output.write(FrameCodec.encode(Frame.Hello(ProtocolConstants.VERSION)))
            output.flush()
            // The laptop may hold its answer while its owner decides whether to trust this phone, so the
            // wait for HELLO_ACK has an end of its own: longer than the laptop's own prompt, after which it
            // answers no by itself, so that the laptop's no is what the user sees rather than this timeout.
            main.postDelayed(slowAnswer, SLOW_ANSWER_MS)
            main.postDelayed(noAnswer, ANSWER_TIMEOUT_MS)
            var ack = readFrame(input)
            // A laptop at security level 2 or 3 asks for its PIN between the trust question and HELLO_ACK, and
            // again after each wrong answer. A level-1 laptop never asks, so for it this loop never runs.
            while (true) {
                val request = ack as? Frame.PinRequest ?: break
                // The person is typing now; the laptop's own 60 s wait is the limit on that, not this side's.
                main.removeCallbacks(slowAnswer)
                main.removeCallbacks(noAnswer)
                val locked = closureFor(request)
                if (locked != null) {
                    close(locked)
                    return
                }
                deliver { it.onPinRequest(this, request.attemptsLeft) }
                ack = readFrame(input)
            }
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
            // The laptop forgets where captures go when a session ends, so every new one is told again.
            CaptureInbox.target?.let { send(it.frame()) }
            val w = thread(name = "edgepad-writer", priority = Thread.MAX_PRIORITY) { writeLoop(output) }
            writer = w
            // A close() that raced the line above found no writer to interrupt.
            if (closed) w.interrupt()
            while (!closed) {
                val frame = readFrame(input)
                if (frame is Frame.Pong) {
                    val ms = (System.nanoTime() - frame.time) / NANOS_PER_MS
                    deliver { it.onRoundTrip(ms) }
                } else if (!CaptureInbox.take(frame, ::send)) {
                    // Asked for here, ahead of the main thread, so the pictures are on their way before any
                    // screen wants them and are cached by the time the macro screen opens.
                    if (wantsIcons(frame)) send(TextKind.WANT_ICONS.frame(iconPx.toString()))
                    deliver { it.onFrame(frame) }
                }
            }
        } catch (e: IOException) {
            // An EOF before HELLO_ACK is the laptop hanging up: its owner said no, or it is too old.
            val closure = closureOf(e, connectReturned, handshakeDone)
            Log.w(TAG, "The link closed as ${closure.cause}", e)
            close(closure)
        } catch (e: SecurityException) {
            close(Closure(Cause.NO_PERMISSION))
        } catch (e: RuntimeException) {
            // A bug on this side, not a fault on the link. Before 3.2 it escaped the thread and took the whole
            // app down with it; ending the one link is the most it should cost.
            Log.e(TAG, "The link's reader failed", e)
            close(Closure(Cause.LOST))
        }
    }

    /** The owner's answer to [Listener.onPairingConfirm]: true only when the laptop said the code matched. */
    fun answerPairing(accepted: Boolean) {
        pairingAnswer.offer(accepted)
    }

    // Runs on the link's thread, which waits here for the owner; a no, a close or the timeout all keep nothing.
    private fun askOwner(): Boolean {
        if (closed) return false
        pairingAnswer.clear()
        deliver { it.onPairingConfirm(this) }
        return try {
            pairingAnswer.poll(CONFIRM_TIMEOUT_MS, TimeUnit.MILLISECONDS) == true && !closed
        } catch (e: InterruptedException) {
            false
        }
    }

    /**
     * Answers a PIN_REQUEST. Written straight onto the socket, because [send] drops everything until the
     * handshake is done, and under [writeLock] on a short thread of its own, so a stalled link never blocks the
     * main thread. Nothing here holds [pin] once the answer is written. Zeroing the encoded frame afterwards is
     * not a wipe of the digits: they also sit in the [Pin]'s immutable String and in the codec's buffer until
     * the garbage collector reclaims them, which nothing on the JVM can hurry.
     */
    fun answerPin(pin: Pin) {
        val s = streams
        if (s == null || closed || handshakeDone) return
        val bytes = FrameCodec.encode(Frame.PinAnswer(pin))
        // The laptop has something to check now, so the wait for its answer has an end again.
        main.postDelayed(noAnswer, ANSWER_TIMEOUT_MS)
        thread(name = "edgepad-pin") {
            try {
                synchronized(writeLock) {
                    s.output.write(bytes)
                    s.output.flush()
                }
            } catch (e: IOException) {
                Log.w(TAG, "The PIN answer could not be written", e)
                close(Closure(classifyFailure(connectReturned = true, handshakeDone = handshakeDone)))
            } finally {
                bytes.fill(0)
            }
        }
    }

    fun close(closure: Closure) {
        synchronized(lock) {
            if (closed) return
            closed = true
        }
        main.removeCallbacks(slowAnswer)
        main.removeCallbacks(noAnswer)
        transport.close()
        pairingAnswer.offer(false)
        writer?.interrupt()
        outbox.clear()
        CaptureInbox.abort()
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
                // Everything queued behind the first frame goes out now, the whole backlog and not just the
                // next few frames of it, so a run of moves really does collapse into one (see Coalesce)
                // instead of replaying late. The buffer is written whenever it fills, so a long backlog is
                // several writes, never a bigger buffer. A transport with a write pace holds a run of motion
                // briefly first, and never an edge (see PacedBatch).
                PacedBatch.collect(outbox.take(), outbox, transport.writePaceMs, System::nanoTime, pending)
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
            Log.w(TAG, "The link's writer failed", e)
            close(Closure(Cause.LOST))
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

        /** How long the owner has to say whether the laptop accepted the pairing code. */
        const val CONFIRM_TIMEOUT_MS = 60_000L
        const val NANOS_PER_MS = 1_000_000.0
    }
}

/**
 * Why a link failed with an IOException, from how far it got: before [connectReturned] the laptop never
 * took the connection, before [handshakeDone] it took it and hung up, and after that a working link
 * dropped. Two flags rather than the exception, so a test can cover it without a socket.
 */
internal fun classifyFailure(
    connectReturned: Boolean,
    handshakeDone: Boolean,
): LaptopLink.Cause =
    when {
        !connectReturned -> LaptopLink.Cause.UNREACHABLE
        handshakeDone -> LaptopLink.Cause.LOST
        else -> LaptopLink.Cause.REFUSED
    }

/**
 * What an IOException ends the link with: the transport's own reason when it reached the laptop and was turned
 * away, otherwise [classifyFailure]'s reading of how far the link got.
 */
internal fun closureOf(
    e: IOException,
    connectReturned: Boolean,
    handshakeDone: Boolean,
): LaptopLink.Closure =
    (e as? LinkFailure)?.closure ?: LaptopLink.Closure(classifyFailure(connectReturned, handshakeDone))

/**
 * What a PIN_REQUEST ends the link with: a lock-out (no attempts left and a wait) closes it as
 * [LaptopLink.Cause.PIN_LOCKED] with the wait in seconds, and a question leaves it open. Pure, so a test can
 * cover it without a socket.
 */
internal fun closureFor(request: Frame.PinRequest): LaptopLink.Closure? =
    if (request.attemptsLeft == 0 && request.lockedSeconds > 0) {
        LaptopLink.Closure(LaptopLink.Cause.PIN_LOCKED, request.lockedSeconds.toString())
    } else {
        null
    }

/**
 * True for a macro list with at least one name in it: the cue to ask the laptop for the icons. The list
 * arrives once the handshake is done and again whenever a macro is added or edited, so asking on every one
 * of them fetches the pictures as soon as the link is up and again for a new or changed macro. Pure, so a
 * test can cover it without a socket.
 */
internal fun wantsIcons(frame: Frame): Boolean =
    frame is Frame.Text && frame.kind == TextKind.MACROS.id && frame.text.isNotBlank()

private val ICON_TIERS = intArrayOf(128, 256, 384, 512)

/** The smallest icon size the laptop offers that is at least [px], or the largest when none is. */
internal fun snapUp(px: Int): Int = ICON_TIERS.firstOrNull { it >= px } ?: ICON_TIERS.last()
