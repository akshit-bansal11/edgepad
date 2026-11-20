package me.akshitbansal.edgepad.link.blob

import me.akshitbansal.edgepad.link.DashWire
import me.akshitbansal.edgepad.link.GithubRow
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame

/**
 * Puts the laptop's GitHub list back together on the link's reading thread and hands the parsed rows over. Only
 * [BlobKind.GITHUB] is taken and nothing over [MAX_BYTES]: anything else is drained and dropped by the receiver, and
 * the next blob starts clean. Nothing is kept on disk, and each finished blob replaces the one before it.
 *
 * @param completed Handed the rows of each checked blob, on the reading thread. It must not touch the UI.
 */
class GitHubSink(
    private val completed: (List<GithubRow>) -> Unit,
) {
    private val receiver =
        BlobReceiver({ kind -> if (kind == BlobKind.GITHUB.id) MAX_BYTES.toLong() else null }) { _, data, _ ->
            completed(DashWire.github(data))
            true
        }

    /** One blob frame from this link's laptop. */
    fun take(frame: Frame) {
        receiver.take(frame)
    }

    companion object {
        /** The most a GitHub blob may hold: 8 KiB. */
        const val MAX_BYTES = 8 * 1024
    }
}
