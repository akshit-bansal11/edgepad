package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.BlobKind

/**
 * How [LinkService] and [me.akshitbansal.edgepad.ShareActivity], which outlive the activity's views, reach the two
 * things they must do on their own: end every link when the notification's Disconnect is tapped, and send what another
 * app shared. Hooks and not the manager itself, so nothing here can hold an activity: the activity sets them in onCreate
 * over the manager alone and clears them when it is really going away. Called on the main thread, where the manager
 * lives.
 */
object LinkHooks {
    @Volatile
    var disconnectAll: (() -> Unit)? = null

    /** The selected laptop if it is connected, or null. */
    @Volatile
    var shareTarget: (() -> ShareTarget?)? = null
}

/**
 * The laptop a share would go to. [send] is null while the laptop will not take a clipboard (its Share clipboard or this
 * phone's is off); otherwise it blocks for the whole transfer, so it is called off the main thread, and says whether it went.
 */
class ShareTarget(
    val name: String,
    val send: ((BlobKind, ByteArray) -> Boolean)?,
)
