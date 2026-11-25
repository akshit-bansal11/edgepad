package me.akshitbansal.edgepad.link

import android.net.Uri
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame
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

    /** Sends one frame to the selected laptop; false when nothing is connected. For the tile and the notification. */
    @Volatile
    var sendToSelected: ((Frame) -> Boolean)? = null

    /** The selected laptop for the Quick Settings tile, or null when nothing is connected. */
    @Volatile
    var tileStatus: (() -> TileStatus?)? = null

    /** Shares on their way to a laptop right now. While any is, the app leaving does not end the links. */
    val sharing = AtomicInteger()
}

/** What the Quick Settings tile shows: the laptop it would send to. */
data class TileStatus(
    val laptopName: String,
)

/**
 * The links of an activity Android destroyed by itself (memory pressure, "Don't keep activities") while Stay connected
 * held the process: the next activity adopts them as it would after a rotation. Process memory only, and no activity in
 * it: the manager's events are [KeptEvents] while it waits here. Emptied by the next activity, or by
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

/**
 * The events of a manager waiting in [LinkSession]: no screen to tell, but nothing calls the activity's syncService
 * either, so when the last link closes this ends what that would have: [onEmpty] stops the service and its retries.
 */
class KeptEvents(
    private val manager: LaptopConnections,
    private val onEmpty: () -> Unit,
) : LaptopConnections.Events by LaptopConnections.Events.None {
    override fun onClosed(
        laptop: Laptop,
        closure: LaptopLink.Closure,
    ) {
        if (manager.laptops.none { it.link != null }) onEmpty()
    }
}

/** What the linger does when it runs out: end the links, or wait for a share still going. */
enum class LingerAction {
    CLOSE,
    WAIT,
}

/** [LingerAction.WAIT] while any of [sharing] shares is still going, else [LingerAction.CLOSE]. */
internal fun lingerAction(sharing: Int): LingerAction = if (sharing > 0) LingerAction.WAIT else LingerAction.CLOSE

/**
 * The laptop a share would go to. [send] is null while the laptop will not take a clipboard (its Share clipboard or this
 * phone's is off); otherwise it blocks for the whole transfer, so it is called off the main thread, and says whether it went.
 */
class ShareTarget(
    val name: String,
    val send: ((BlobKind, ByteArray) -> Boolean)?,
    /**
     * Sends picked documents as file transfers, one after another, and blocks until the laptop has answered the last,
     * so off the main thread. True when every one arrived.
     */
    val sendFiles: ((List<Uri>) -> Boolean)? = null,
)
