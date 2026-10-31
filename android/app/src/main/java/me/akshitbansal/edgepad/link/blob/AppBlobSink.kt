package me.akshitbansal.edgepad.link.blob

import me.akshitbansal.edgepad.link.AppCatalogState
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame

/**
 * Puts a laptop's app list and its icons back together on the link's reading thread, and hands each finished blob over
 * with its label: an icon's label is the token of the app it belongs to, and [BlobReceiver] does not keep labels, so
 * this keeps the one from the BLOB_BEGIN in flight. Nothing is trusted: a kind that is not an app blob and a blob over
 * [AppCatalogState]'s caps are drained and dropped by the receiver, and the next one starts clean.
 *
 * @param completed Handed the kind, the label and the checked bytes, on the reading thread. It must not touch the UI.
 */
class AppBlobSink(
    private val completed: (BlobKind, String, ByteArray) -> Unit,
) {
    private var label = ""
    private val receiver =
        BlobReceiver({ cap(it) }) { kind, data, _ ->
            val blob = BlobKind.of(kind)
            if (blob != null) completed(blob, label, data)
            blob != null
        }

    /** One blob frame from this link's laptop. */
    fun take(frame: Frame) {
        if (frame is Frame.BlobBegin) label = frame.label
        receiver.take(frame)
    }

    private companion object {
        fun cap(kind: Int): Long? =
            when (BlobKind.of(kind)) {
                BlobKind.APP_LIST -> AppCatalogState.MAX_LIST_BYTES.toLong()
                BlobKind.APP_ICON -> AppCatalogState.MAX_ICON_BYTES.toLong()
                else -> null
            }
    }
}
