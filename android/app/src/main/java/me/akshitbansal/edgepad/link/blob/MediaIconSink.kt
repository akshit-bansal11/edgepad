package me.akshitbansal.edgepad.link.blob

import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame

/**
 * Puts a laptop's media icons back together on the link's reading thread and hands each finished one over with its
 * label, the app's name or [MediaIconStore.ARTWORK]. [BlobReceiver] does not keep labels, so this keeps the one from
 * the BLOB_BEGIN in flight. A kind that is not [BlobKind.MEDIA_ICON] and a blob over [MAX_BYTES] are drained and
 * dropped by the receiver.
 *
 * @param completed Handed the label and the checked bytes, on the reading thread. It must not touch the UI.
 */
class MediaIconSink(
    private val completed: (String, ByteArray) -> Unit,
) {
    private var label = ""
    private val receiver =
        BlobReceiver({ kind -> if (kind == BlobKind.MEDIA_ICON.id) MAX_BYTES.toLong() else null }) { _, data, _ ->
            completed(label, data)
            true
        }

    /** One blob frame from this link's laptop. */
    fun take(frame: Frame) {
        if (frame is Frame.BlobBegin) label = frame.label
        receiver.take(frame)
    }

    companion object {
        /** A 64 px PNG is a few kilobytes; this is the most one blob may hold. */
        const val MAX_BYTES = 24 * 1024
    }
}
