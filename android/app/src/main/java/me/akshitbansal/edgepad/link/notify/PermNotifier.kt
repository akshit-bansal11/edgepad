package me.akshitbansal.edgepad.link.notify

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import me.akshitbansal.edgepad.MainActivity
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.link.PermAsk

/**
 * Says an AI agent waits for an approval while Edgepad is not on screen. It carries no laptop text at all, and the
 * lock screen shows only the generic line; it has no buttons, so nothing is allowed from a notification. A tap opens
 * the app, where the approval sheet answers it. Never posts without permission.
 */
class PermNotifier(
    private val context: Context,
) {
    private val posted = HashSet<String>()

    /**
     * Matches the notifications to [waiting] (the sheet's unanswered asks): each one gets a notification while
     * [foreground] is false; an ask that was answered or went, or the app coming to the front, cancels its own.
     */
    fun sync(
        waiting: List<PermAsk>,
        foreground: Boolean,
    ) {
        val notifications = context.getSystemService(NotificationManager::class.java) ?: return
        val keep = if (foreground) emptySet() else waiting.map { it.id }.toSet()
        posted.filter { it !in keep }.forEach {
            notifications.cancel(it, NOTICE_ID)
            posted.remove(it)
        }
        if (foreground || !allowed(notifications)) return
        waiting.filter { it.id !in posted }.forEach {
            notifications.notify(it.id, NOTICE_ID, build())
            posted.add(it.id)
        }
    }

    private fun allowed(notifications: NotificationManager): Boolean {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            return false
        }
        return notifications.areNotificationsEnabled()
    }

    private fun build(): Notification {
        val notifications = context.getSystemService(NotificationManager::class.java)
        notifications?.createNotificationChannel(
            NotificationChannel(CHANNEL, context.getString(R.string.perm_channel), NotificationManager.IMPORTANCE_HIGH),
        )
        val title = context.getString(R.string.perm_notif)
        val open =
            PendingIntent.getActivity(
                context,
                0,
                Intent(context, MainActivity::class.java),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
        val lockScreen =
            Notification
                .Builder(context, CHANNEL)
                .setSmallIcon(R.drawable.ic_laptop)
                .setContentTitle(title)
                .build()
        return Notification
            .Builder(context, CHANNEL)
            .setSmallIcon(R.drawable.ic_laptop)
            .setContentTitle(title)
            .setCategory(Notification.CATEGORY_REMINDER)
            .setVisibility(Notification.VISIBILITY_PRIVATE)
            .setPublicVersion(lockScreen)
            .setContentIntent(open)
            .setAutoCancel(true)
            .build()
    }

    private companion object {
        const val CHANNEL = "perm"

        /** With the ask's id as the tag. 1 to 5 are the service, install, update, laptop notices and transfers. */
        const val NOTICE_ID = 6
    }
}
