package me.akshitbansal.edgepad.link.blob

import me.akshitbansal.edgepad.link.GitData
import me.akshitbansal.edgepad.link.GitWire
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame

/**
 * Puts one git host's page back together on the link's reading thread and hands the parsed page over. Only
 * [BlobKind.GIT] is taken and nothing over [MAX_BYTES]: anything else is drained and dropped by the receiver, and
 * the next blob starts clean. Nothing is kept on disk, and each finished blob replaces the one before it for its host.
 *
 * @param completed Handed the page of each checked blob, on the reading thread. It must not touch the UI.
 */
class GitSink(
    private val completed: (GitData) -> Unit,
) {
    private val receiver =
        BlobReceiver({ kind -> if (kind == BlobKind.GIT.id) MAX_BYTES.toLong() else null }) { _, data, _ ->
            completed(GitWire.parse(data))
            true
        }

    /** One blob frame from this link's laptop. */
    fun take(frame: Frame) {
        receiver.take(frame)
    }

    companion object {
        /** The most a git blob may hold: 16 KiB. */
        const val MAX_BYTES = 16 * 1024
    }
}
