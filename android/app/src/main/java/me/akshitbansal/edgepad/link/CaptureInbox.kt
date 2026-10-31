package me.akshitbansal.edgepad.link

import android.content.ContentResolver
import android.os.Handler
import android.os.Looper
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.CaptureStatus
import me.akshitbansal.edgepad.protocol.CaptureTarget
import me.akshitbansal.edgepad.protocol.FileStatus
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.TextKind
import java.util.concurrent.ConcurrentHashMap

/**
 * Where a capture the laptop sends lands, between the link that reads it and the capture screen that shows it. One per
 * app rather than per screen, because a recording arrives when it stops, which may be long after the person left the
 * capture screen. The screen installs it ([install]); until then a FILE_BEGIN is refused and the laptop keeps the file.
 *
 * Permits belong to the link they were granted on, each with its own [FileInbox]: a permit granted for one laptop can
 * never be spent by another's FILE_BEGIN, and a recording left running on a laptop no longer selected still lands,
 * because moving [owner] touches nobody's permits. A link with no inbox here was never asked, and is refused.
 *
 * [take] runs on the link's reading thread, so a file never passes through the main thread. [listener] and [last] are
 * main-thread only.
 */
object CaptureInbox {
    private val main by lazy { Handler(Looper.getMainLooper()) }

    /** Hands a step to the main thread. Tests run it in place, since a JVM test has no main looper. */
    @Volatile internal var poster: (() -> Unit) -> Unit = { block -> main.post { block() } }

    /** Set once the capture screen has been built. */
    @Volatile internal var store: FileInbox.Store? = null

    private val inboxes = ConcurrentHashMap<Any, FileInbox>()

    /** The [ManagedLink.id] the capture screen sends to, so the link a new permit is for. */
    @Volatile var owner: Any? = null

    /**
     * What this phone last chose, sent again whenever a link opens; null until the capture screen has been built.
     * Laptop turns the inbox off: every permit goes and a file on its way in is thrown away.
     */
    @Volatile var target: CaptureTarget? = null
        set(value) {
            field = value
            inboxes.values.forEach { it.toPhone = value == CaptureTarget.PHONE }
        }

    /** Told of every step of every file, on the main thread. The capture screen sets it while it is showing. */
    var listener: ((FileInbox.Progress) -> Unit)? = null

    /** The last step, so a capture screen built later can say how the last capture went. */
    var last: FileInbox.Progress? = null
        private set

    fun install(
        resolver: ContentResolver,
        chosen: CaptureTarget,
    ) {
        if (store == null) store = GalleryStore(resolver)
        target = chosen
    }

    /** This phone sent a capture action: with the target at Phone, the laptop may send one file for it. */
    fun permit(action: ActionId) {
        val link = owner ?: return
        val files = store ?: return
        inboxes
            .getOrPut(link) {
                FileInbox(files, report = { step -> post(step) }).apply {
                    toPhone = target == CaptureTarget.PHONE
                }
            }.permit(action)
    }

    /**
     * The capture screen closed: the [owner]'s file on its way in is thrown away. Permits stay, for a recording still
     * running. Another link's file carries on.
     */
    fun cancel() {
        owner?.let { inboxes[it] }?.abort()
    }

    /**
     * True when [frame] was a file frame or a capture status, which go nowhere else. [link] is the [ManagedLink.id] it
     * came on, and [send] carries the answer back on that link.
     */
    fun take(
        frame: Frame,
        link: Any,
        send: (Frame) -> Unit,
    ): Boolean {
        when (frame) {
            is Frame.FileBegin, is Frame.FileChunk, is Frame.FileEnd -> {
                val inbox = inboxes[link]
                if (inbox != null) {
                    inbox.take(frame)?.let(send)
                } else if (frame is Frame.FileBegin) {
                    send(FileStatus.REJECTED.frame())
                }
            }

            is Frame.Text -> {
                if (frame.kind != TextKind.CAPTURE_STATUS.id) return false
                val inbox = inboxes[link]
                inbox?.settle()
                if (inbox != null || link === owner) post(FileInbox.Progress.Kept(CaptureStatus.of(frame.text)))
            }

            else -> {
                return false
            }
        }
        return true
    }

    /**
     * [link] closed. A file that was half here is thrown away (the laptop never heard SAVED and keeps it), and its
     * permits and per-connection counts go with it.
     */
    fun abort(link: Any) {
        inboxes.remove(link)?.reset()
    }

    private fun post(step: FileInbox.Progress) {
        poster {
            last = step
            listener?.invoke(step)
        }
    }
}
