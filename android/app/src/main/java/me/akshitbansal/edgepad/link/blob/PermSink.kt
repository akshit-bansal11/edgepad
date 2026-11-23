package me.akshitbansal.edgepad.link.blob

import me.akshitbansal.edgepad.link.DashWire
import me.akshitbansal.edgepad.link.PermAsk
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame

/**
 * Puts one approval request back together on the link's reading thread and hands the decoded ask over. Only
 * [BlobKind.PERM_ASK] is taken, nothing over [MAX_BYTES]; a label or body that does not decode is dropped. The label
 * (`id/f`) is kept from the BLOB_BEGIN in flight, as [MediaIconSink] does.
 *
 * @param completed Handed each decoded ask, on the reading thread. It must not touch the UI.
 */
class PermSink(
    private val completed: (PermAsk) -> Unit,
) {
    private var label = ""
    private val receiver =
        BlobReceiver({ kind -> if (kind == BlobKind.PERM_ASK.id) MAX_BYTES.toLong() else null }) { _, data, _ ->
            val (id, complete) = DashWire.permLabel(label) ?: return@BlobReceiver false
            val ask = DashWire.permAsk(id, complete, data) ?: return@BlobReceiver false
            completed(ask)
            true
        }

    /** One blob frame from this link's laptop. */
    fun take(frame: Frame) {
        if (frame is Frame.BlobBegin) label = frame.label
        receiver.take(frame)
    }

    companion object {
        /** The most an approval blob may hold: 4 KiB, the same cap [DashWire.permAsk] enforces. */
        const val MAX_BYTES = 4 * 1024
    }
}
