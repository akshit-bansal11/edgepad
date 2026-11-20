package me.akshitbansal.edgepad.link.notify

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import me.akshitbansal.edgepad.R

/** Posts one system notification for each laptop notification: never without permission, replacing by id. */
class PhoneNotifier(
    private val context: Context,
) {
    /** Posts one notification; a missing permission or a disabled app drops it. Safe on any thread. */
    fun post(
        id: String,
        app: String,
        title: String,
        body: String,
    ) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            return
        }
        val notifications = context.getSystemService(NotificationManager::class.java) ?: return
        if (!notifications.areNotificationsEnabled()) return
        val name = context.getString(R.string.notify_channel)
        notifications.createNotificationChannel(
            NotificationChannel(CHANNEL, name, NotificationManager.IMPORTANCE_DEFAULT),
        )
        val notice =
            Notification
                .Builder(context, CHANNEL)
                .setSmallIcon(R.drawable.ic_laptop)
                .setSubText(app)
                .setContentTitle(title)
                .setContentText(body)
                .setStyle(Notification.BigTextStyle().bigText(body))
                .setAutoCancel(true)
                .build()
        notifications.notify(id, NOTICE_ID, notice)
    }

    private companion object {
        const val CHANNEL = "notify"

        /** With the laptop's id as the tag, so the same id replaces. 1 to 3 are the service, install and update. */
        const val NOTICE_ID = 4
    }
}
