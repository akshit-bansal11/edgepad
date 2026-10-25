package me.akshitbansal.edgepad.link

import android.content.ContentResolver
import android.os.Handler
import android.os.Looper
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
 * [take] runs on the link's reading thread, so a file never passes through the main thread. [listener] and [last] are
 * main-thread only.
 */
object CaptureInbox {
    private val main by lazy { Handler(Looper.getMainLooper()) }

    @Volatile private var inbox: FileInbox? = null

    /** What this phone last chose, sent again whenever a link opens; null until the capture screen has been built. */
    @Volatile var target: CaptureTarget? = null

    /** Told of every step of every file, on the main thread. The capture screen sets it while it is showing. */
    var listener: ((FileInbox.Progress) -> Unit)? = null

    /** The last step, so a capture screen built later can say how the last capture went. */
    var last: FileInbox.Progress? = null
        private set

    fun install(
        resolver: ContentResolver,
        chosen: CaptureTarget,
    ) {
        target = chosen
        if (inbox == null) inbox = FileInbox(GalleryStore(resolver)) { step -> post(step) }
    }

    /**
     * True when [frame] was a file frame or a capture status, which go nowhere else. [send] carries the answer back.
     */
    fun take(
        frame: Frame,
        send: (Frame) -> Unit,
    ): Boolean {
        when (frame) {
            is Frame.FileBegin, is Frame.FileChunk, is Frame.FileEnd -> {
                val current = inbox
                if (current != null) {
                    current.take(frame)?.let(send)
                } else if (frame is Frame.FileBegin) {
                    send(FileStatus.REJECTED.frame())
                }
            }

            is Frame.Text -> {
                if (frame.kind != TextKind.CAPTURE_STATUS.id) return false
                post(FileInbox.Progress.Kept(CaptureStatus.of(frame.text)))
            }

            else -> {
                return false
            }
        }
        return true
    }

    /** The link closed. A file that was half here is thrown away; the laptop never heard SAVED and keeps it. */
    fun abort() {
        inbox?.abort()
    }

    private fun post(step: FileInbox.Progress) {
        main.post {
            last = step
            listener?.invoke(step)
        }
    }
}
