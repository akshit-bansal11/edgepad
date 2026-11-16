package me.akshitbansal.edgepad.link

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.graphics.drawable.Icon
import android.os.IBinder
import android.util.Log
import me.akshitbansal.edgepad.MainActivity
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.Frame

private const val DISCONNECT_ACTION = "me.akshitbansal.edgepad.DISCONNECT"

/**
 * Keeps the process, and so the links, alive while Edgepad is minimized: a foreground service that does nothing
 * but exist and show one notification. The links themselves stay [LaptopConnections]'s; this holds no state of
 * their own.
 *
 * It runs while at least one laptop is connected and Stay connected is on. The activity starts it only while it is
 * on screen, because Android 12 and later refuse to start a foreground service from the background, and stops it
 * when the last link goes. The type is connectedDevice, whose Android 14 runtime prerequisite is one of a short
 * list of permissions; CHANGE_WIFI_MULTICAST_STATE, already declared for finding laptops, is on it, as is
 * BLUETOOTH_CONNECT.
 *
 * It also runs while a link that dropped is being won back, with a notification that says so, so that the
 * retries go on with the app minimized; they run out after a few attempts like any others, and the service with them.
 *
 * The notification's Disconnect ends every link through [LinkHooks] and stops this service.
 */
class LinkService : Service() {
    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(
        intent: Intent?,
        flags: Int,
        startId: Int,
    ): Int {
        if (intent == null || intent.action == DISCONNECT_ACTION) {
            // Whether or not an activity is there to answer, the service has no reason left to run.
            if (intent != null) LinkHooks.disconnectAll?.invoke()
            stopSelf()
            return START_NOT_STICKY
        }
        // A media button is a tap and nothing else: it must not rebuild the notification from extras it lacks.
        val media = NoticeAction.entries.firstOrNull { it.intentAction == intent.action }
        if (media != null) {
            media.frame?.let { LinkHooks.sendToSelected?.invoke(it) }
            return START_NOT_STICKY
        }
        val notice =
            LinkNotice(
                intent.getStringExtra(EXTRA_NAME).orEmpty(),
                intent.getIntExtra(EXTRA_OTHERS, 0),
                intent.getBooleanExtra(EXTRA_RECONNECTING, false),
                intent.getBooleanExtra(EXTRA_SHARING, false),
            )
        startForeground(NOTICE_ID, build(this, notice), ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        return START_NOT_STICKY
    }

    companion object {
        private const val CHANNEL = "link"
        private const val NOTICE_ID = 1
        private const val EXTRA_NAME = "name"
        private const val EXTRA_OTHERS = "others"
        private const val EXTRA_RECONNECTING = "reconnecting"
        private const val EXTRA_SHARING = "sharing"

        /**
         * Starts the service with [notice], or rewrites its notification when it is already running; [running] is the
         * caller's own knowledge of which. Only from the foreground: the caller checks, as the start is refused
         * otherwise. False when the start was refused all the same: Android 12 and later throw
         * ForegroundServiceStartNotAllowedException, an IllegalStateException, in more cases than the caller can see.
         */
        fun show(
            context: Context,
            notice: LinkNotice,
            running: Boolean,
        ): Boolean {
            if (running) {
                context.getSystemService(NotificationManager::class.java)?.notify(NOTICE_ID, build(context, notice))
                return true
            }
            return try {
                context.startForegroundService(
                    Intent(context, LinkService::class.java)
                        .putExtra(EXTRA_NAME, notice.name)
                        .putExtra(EXTRA_OTHERS, notice.others)
                        .putExtra(EXTRA_RECONNECTING, notice.reconnecting)
                        .putExtra(EXTRA_SHARING, notice.sharing),
                )
                true
            } catch (e: RuntimeException) {
                Log.w("Edgepad", "The link service was not allowed to start", e)
                false
            }
        }

        fun stop(context: Context) {
            context.stopService(Intent(context, LinkService::class.java))
        }

        private fun build(
            context: Context,
            notice: LinkNotice,
        ): Notification {
            // Low importance: no sound and no peek, because this is a status and never news.
            val name = context.getString(R.string.link_channel)
            val channel = NotificationChannel(CHANNEL, name, NotificationManager.IMPORTANCE_LOW)
            channel.setShowBadge(false)
            context.getSystemService(NotificationManager::class.java)?.createNotificationChannel(channel)
            val title =
                when {
                    notice.reconnecting -> {
                        context.getString(R.string.link_notice_reconnecting, notice.name)
                    }

                    notice.others > 0 -> {
                        context.resources
                            .getQuantityString(R.plurals.link_notice_more, notice.others, notice.name, notice.others)
                    }

                    else -> {
                        context.getString(R.string.link_notice, notice.name)
                    }
                }
            val open =
                PendingIntent.getActivity(
                    context,
                    0,
                    Intent(context, MainActivity::class.java),
                    PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
                )
            // The Disconnect button ends every link, and a laptop being won back too, so it says all of that.
            val disconnectLabel =
                when {
                    notice.reconnecting -> R.string.lost_stop
                    notice.others > 0 -> R.string.device_disconnect_all
                    else -> R.string.device_disconnect
                }
            val actions =
                noticeActions(notice).map {
                    val label = if (it == NoticeAction.DISCONNECT) disconnectLabel else it.label
                    val tap =
                        PendingIntent.getService(
                            context,
                            0,
                            Intent(context, LinkService::class.java).setAction(it.intentAction),
                            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
                        )
                    Notification.Action
                        .Builder(Icon.createWithResource(context, it.icon), context.getString(label), tap)
                        .build()
                }
            val text =
                context.getString(
                    when {
                        notice.reconnecting -> R.string.link_notice_reconnecting_text
                        notice.sharing -> R.string.link_notice_text_clipboard
                        else -> R.string.link_notice_text
                    },
                )
            val builder =
                Notification
                    .Builder(context, CHANNEL)
                    .setSmallIcon(R.drawable.ic_laptop)
                    .setContentTitle(title)
                    .setContentText(text)
                    .setContentIntent(open)
                    .setOngoing(true)
                    .setOnlyAlertOnce(true)
                    .setCategory(Notification.CATEGORY_SERVICE)
            actions.forEach { builder.addAction(it) }
            // Three media buttons show collapsed; while reconnecting the one Stop button is all there is.
            if (actions.size > 1) {
                builder.setStyle(Notification.MediaStyle().setShowActionsInCompactView(0, 1, 2))
            } else {
                builder.setStyle(Notification.BigTextStyle().bigText(text))
            }
            return builder.build()
        }
    }
}

/** The buttons of the notification, in the order they appear. */
internal enum class NoticeAction(
    val intentAction: String,
    val icon: Int,
    val label: Int,
    val frame: Frame?,
) {
    VOLUME_DOWN(
        "me.akshitbansal.edgepad.VOLUME_DOWN",
        R.drawable.ic_volume_down,
        R.string.notice_volume_down,
        ActionId.VOLUME_DOWN.frame(),
    ),
    PLAY_PAUSE(
        "me.akshitbansal.edgepad.PLAY_PAUSE",
        R.drawable.ic_play,
        R.string.notice_play_pause,
        ActionId.PLAY_PAUSE.frame(),
    ),
    VOLUME_UP(
        "me.akshitbansal.edgepad.VOLUME_UP",
        R.drawable.ic_volume_up,
        R.string.notice_volume_up,
        ActionId.VOLUME_UP.frame(),
    ),
    DISCONNECT(DISCONNECT_ACTION, R.drawable.ic_close, R.string.device_disconnect, null),
}

/** Only Disconnect (as Stop) while a laptop is being won back; all four while connected. */
internal fun noticeActions(notice: LinkNotice): List<NoticeAction> =
    if (notice.reconnecting) listOf(NoticeAction.DISCONNECT) else NoticeAction.entries
