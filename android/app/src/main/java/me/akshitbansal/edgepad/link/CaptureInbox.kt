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

/**
 * Where a capture the laptop sends lands, between the link that reads it and the capture screen that shows it. One per
 * app rather than per screen, because a recording arrives when it stops, which may be long after the person left the
 * capture screen. The screen installs it ([install]); until then a FILE_BEGIN is refused and the laptop keeps the file.
 *
 * Permits belong to one link, [owner]: the one the capture screen sends to. A permit granted for one laptop must never
 * be spent by another's FILE_BEGIN, so a file frame from any other link is refused here.
 *
 * [take] runs on the link's reading thread, so a file never passes through the main thread. [listener] and [last] are
 * main-thread only.
 */
object CaptureInbox {
    private val main by lazy { Handler(Looper.getMainLooper()) }

    @Volatile internal var inbox: FileInbox? = null

    /** The [ManagedLink.id] whose captures are wanted. Moving on takes the permits and a file half here with it. */
    @Volatile var owner: Any? = null
        set(value) {
            if (value !== field) inbox?.reset()
            field = value
        }

    /**
     * What this phone last chose, sent again whenever a link opens; null until the capture screen has been built.
     * Laptop turns the inbox off: every permit goes and a file on its way in is thrown away.
     */
    @Volatile var target: CaptureTarget? = null
        set(value) {
            field = value
            inbox?.toPhone = value == CaptureTarget.PHONE
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
        if (inbox == null) inbox = FileInbox(GalleryStore(resolver), report = { step -> post(step) })
        target = chosen
    }

    /** This phone sent a capture action: with the target at Phone, the laptop may send one file for it. */
    fun permit(action: ActionId) {
        inbox?.permit(action)
    }

    /** The capture screen closed: a file on its way in is thrown away. Permits stay, for a recording still running. */
    fun cancel() {
        inbox?.abort()
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
                val current = inbox.takeIf { link === owner }
                if (current != null) {
                    current.take(frame)?.let(send)
                } else if (frame is Frame.FileBegin) {
                    send(FileStatus.REJECTED.frame())
                }
            }

            is Frame.Text -> {
                if (frame.kind != TextKind.CAPTURE_STATUS.id) return false
                if (link === owner) {
                    inbox?.settle()
                    post(FileInbox.Progress.Kept(CaptureStatus.of(frame.text)))
                }
            }

            else -> {
                return false
            }
        }
        return true
    }

    /**
     * [link] closed. If it was the [owner], a file that was half here is thrown away (the laptop never heard SAVED and
     * keeps it), and the permits and the per-connection counts go with it.
     */
    fun abort(link: Any) {
        if (link === owner) inbox?.reset()
    }

    private fun post(step: FileInbox.Progress) {
        main.post {
            last = step
            listener?.invoke(step)
        }
    }
}
