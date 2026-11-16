package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.BlobKind
import java.util.concurrent.atomic.AtomicInteger

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

    /** Shares on their way to a laptop right now. While any is, the app leaving does not end the links. */
    val sharing = AtomicInteger()
}

/**
 * The links of an activity Android destroyed by itself (memory pressure, "Don't keep activities") while Stay connected
 * held the process: the next activity adopts them as it would after a rotation. Process memory only, and no activity in
 * it: the manager's events are [LaptopConnections.Events.None] while it waits here. Emptied by the next activity, or by
 * the notification's Disconnect, so nothing is held once the service has no reason to run.
 */
object LinkSession {
    class Kept(
        val manager: LaptopConnections,
        val clipboard: ClipboardShare,
        val apps: AppLinks,
    )

    @Volatile
    var kept: Kept? = null
}

/** What the linger does when it runs out: end the links, or wait for a share still going. */
enum class LingerAction {
    CLOSE,
    WAIT,
}

internal fun lingerAction(sharing: Int): LingerAction = if (sharing > 0) LingerAction.WAIT else LingerAction.CLOSE

/**
 * The laptop a share would go to. [send] is null while the laptop will not take a clipboard (its Share clipboard or this
 * phone's is off); otherwise it blocks for the whole transfer, so it is called off the main thread, and says whether it went.
 */
class ShareTarget(
    val name: String,
    val send: ((BlobKind, ByteArray) -> Boolean)?,
)
