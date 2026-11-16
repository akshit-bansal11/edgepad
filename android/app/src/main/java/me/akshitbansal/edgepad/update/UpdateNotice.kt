package me.akshitbansal.edgepad.update

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

/** The system notification for an available update: at most one per version, and never without permission. */
object UpdateNotice {
    private const val CHANNEL = "update"

    /** Apart from LinkService's notification, which is 1, and the install question, which is 2. */
    private const val NOTICE_ID = 3

    /** Posts the notice for [version]; true when it was posted. Called from the daily check, so no timer of its own. */
    fun post(
        context: Context,
        version: String,
    ): Boolean {
        val choice = UpdateChoice(context)
        if (choice.wasNotified(version)) return false
        val notifications = context.getSystemService(NotificationManager::class.java) ?: return false
        if (!notifications.areNotificationsEnabled()) return false
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            return false
        }
        val name = context.getString(R.string.updates_title)
        notifications.createNotificationChannel(
            NotificationChannel(CHANNEL, name, NotificationManager.IMPORTANCE_DEFAULT),
        )
        val open =
            PendingIntent.getActivity(
                context,
                0,
                Intent(context, MainActivity::class.java),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
        val notice =
            Notification
                .Builder(context, CHANNEL)
                .setSmallIcon(R.drawable.ic_laptop)
                .setContentTitle(name)
                .setContentText(context.getString(R.string.updates_notify_text, version))
                .setContentIntent(open)
                .setAutoCancel(true)
                .build()
        notifications.notify(NOTICE_ID, notice)
        choice.markNotified(version)
        return true
    }
}
