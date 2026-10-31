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
import me.akshitbansal.edgepad.MainActivity
import me.akshitbansal.edgepad.R

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
 * The notification's Disconnect ends every link through [LinkHooks] and stops this service.
 */
class LinkService : Service() {
    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(
        intent: Intent?,
        flags: Int,
        startId: Int,
    ): Int {
        if (intent == null || intent.action == ACTION_DISCONNECT) {
            // Whether or not an activity is there to answer, the service has no reason left to run.
            if (intent != null) LinkHooks.disconnectAll?.invoke()
            stopSelf()
            return START_NOT_STICKY
        }
        val notice = LinkNotice(intent.getStringExtra(EXTRA_NAME).orEmpty(), intent.getIntExtra(EXTRA_OTHERS, 0))
        startForeground(NOTICE_ID, build(this, notice), ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        return START_NOT_STICKY
    }

    companion object {
        private const val CHANNEL = "link"
        private const val NOTICE_ID = 1
        private const val ACTION_DISCONNECT = "me.akshitbansal.edgepad.DISCONNECT"
        private const val EXTRA_NAME = "name"
        private const val EXTRA_OTHERS = "others"

        /**
         * Starts the service with [notice], or rewrites its notification when it is already running; [running] is the
         * caller's own knowledge of which. Only from the foreground: the caller checks, as the start is refused
         * otherwise.
         */
        fun show(
            context: Context,
            notice: LinkNotice,
            running: Boolean,
        ) {
            if (running) {
                context.getSystemService(NotificationManager::class.java)?.notify(NOTICE_ID, build(context, notice))
            } else {
                context.startForegroundService(
                    Intent(context, LinkService::class.java)
                        .putExtra(EXTRA_NAME, notice.name)
                        .putExtra(EXTRA_OTHERS, notice.others),
                )
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
                if (notice.others > 0) {
                    context.resources
                        .getQuantityString(R.plurals.link_notice_more, notice.others, notice.name, notice.others)
                } else {
                    context.getString(R.string.link_notice, notice.name)
                }
            val open =
                PendingIntent.getActivity(
                    context,
                    0,
                    Intent(context, MainActivity::class.java),
                    PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
                )
            val disconnect =
                PendingIntent.getService(
                    context,
                    0,
                    Intent(context, LinkService::class.java).setAction(ACTION_DISCONNECT),
                    PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
                )
            val action =
                Notification.Action
                    .Builder(
                        Icon.createWithResource(context, R.drawable.ic_close),
                        context.getString(R.string.device_disconnect),
                        disconnect,
                    ).build()
            val text = context.getString(R.string.link_notice_text)
            return Notification
                .Builder(context, CHANNEL)
                .setSmallIcon(R.drawable.ic_laptop)
                .setContentTitle(title)
                .setContentText(text)
                .setStyle(Notification.BigTextStyle().bigText(text))
                .setContentIntent(open)
                .addAction(action)
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .setCategory(Notification.CATEGORY_SERVICE)
                .build()
        }
    }
}
