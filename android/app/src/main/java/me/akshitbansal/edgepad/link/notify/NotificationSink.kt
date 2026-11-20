package me.akshitbansal.edgepad.link.notify

import me.akshitbansal.edgepad.link.blob.BlobReceiver
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame

/**
 * Takes a laptop's notification blobs on the link's reading thread: `app\ntitle\nbody` in UTF-8, the label being the
 * notification's id. Anything malformed or over the caps, anything while [enabled] is false, and anything the
 * [limiter] refuses is dropped. Content is never logged and never kept.
 *
 * @param enabled Whether the phone's setting is on.
 * @param limiter Rate and duplicate limit.
 * @param post Handed the id, app, title and body of one accepted notification. It does the permission check.
 */
class NotificationSink(
    private val enabled: () -> Boolean,
    private val limiter: NotifyLimiter,
    private val post: (String, String, String, String) -> Unit,
) {
    private var label = ""
    private val receiver =
        BlobReceiver({ kind -> if (kind == BlobKind.NOTIFICATION.id) MAX_BYTES.toLong() else null }) { _, data, _ ->
            accept(label, data)
            true
        }

    /** One blob frame from this link's laptop. */
    fun take(frame: Frame) {
        if (frame is Frame.BlobBegin) label = frame.label
        receiver.take(frame)
    }

    private fun accept(
        id: String,
        data: ByteArray,
    ) {
        if (!enabled()) return
        val parts = data.toString(Charsets.UTF_8).split('\n')
        if (parts.size != 3) return
        val (app, title, body) = parts
        if (app.codePointCount(0, app.length) > MAX_TITLE || title.codePointCount(0, title.length) > MAX_TITLE) return
        if (body.codePointCount(0, body.length) > MAX_BODY) return
        if (!limiter.allow(data.contentHashCode())) return
        post(id, app, title, body)
    }

    companion object {
        const val MAX_BYTES = 1024
        const val MAX_TITLE = 60
        const val MAX_BODY = 200
    }
}
