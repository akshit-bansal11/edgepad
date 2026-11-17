package me.akshitbansal.edgepad.link.xfer

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.pm.PackageManager
import android.graphics.drawable.Icon
import me.akshitbansal.edgepad.R

/** Lets a progress through at most once per percent: a notification is rebuilt 100 times a file, not per chunk. */
internal class PercentGate {
    private var last = -1

    /** True when [done] of [total] is a percent not yet let through; the first call and 100 always are. */
    fun pass(
        done: Long,
        total: Long,
    ): Boolean {
        val percent = if (total <= 0) 100 else (done * 100 / total).toInt()
        if (percent == last) return false
        last = percent
        return true
    }
}

/**
 * [name] cut in the middle to at most [max] characters, so that the end, which carries the extension, stays:
 * `quarterly-report-final-v2.pdf` becomes `quarterly-report-fina…v2.pdf`. UX-D39. Pure, so a test covers it.
 */
internal fun middleTruncated(
    name: String,
    max: Int = NAME_MAX,
): String {
    if (name.length <= max) return name
    val dot = name.lastIndexOf('.')
    val hasExtension = dot > 0 && name.length - dot <= MAX_EXTENSION
    var tail = if (hasExtension) name.length - dot + EXTENSION_LEAD else max / 3
    tail = tail.coerceAtMost(max - 2)
    var head = max - 1 - tail
    // Never leave half of a surrogate pair at either cut.
    if (name[head - 1].isHighSurrogate()) head--
    var from = name.length - tail
    if (name[from].isLowSurrogate()) from++
    return name.substring(0, head) + "…" + name.substring(from)
}

private const val NAME_MAX = 28
private const val MAX_EXTENSION = 8

/** Characters kept in front of the extension, so that `v2.pdf` is not all that is left of the end. */
private const val EXTENSION_LEAD = 2

/**
 * The one notification of the transfer running: a progress bar, updated only when a chunk arrives or goes and at most
 * once per percent, with a Cancel action. Never without permission. No timer; the final state replaces the bar.
 *
 * @param cancel What the Cancel action fires; the wiring supplies it. Without one the action is left off.
 */
class XferNotification(
    context: Context,
    private val cancel: PendingIntent? = null,
) {
    private val context: Context = context.applicationContext
    private val gate = PercentGate()

    /** Shows [done] of [total] bytes of [name] if that is a new percent. Safe on any thread. */
    fun progress(
        name: String,
        done: Long,
        total: Long,
        sending: Boolean,
    ) {
        if (!gate.pass(done, total)) return
        val percent = if (total <= 0) 100 else (done * 100 / total).toInt()
        val title =
            context.getString(
                if (sending) R.string.xfer_sending else R.string.xfer_receiving,
                middleTruncated(name),
                percent,
            )
        post(title, ongoing = true, percent = percent)
    }

    /** Replaces the bar with the outcome: saved, or failed. */
    fun finish(
        name: String,
        saved: Boolean,
    ) {
        val title = context.getString(if (saved) R.string.xfer_done else R.string.xfer_failed, middleTruncated(name))
        post(title, ongoing = false)
    }

    private fun post(
        title: String,
        ongoing: Boolean,
        percent: Int = 0,
    ) {
        if (context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            return
        }
        val notifications = context.getSystemService(NotificationManager::class.java) ?: return
        if (!notifications.areNotificationsEnabled()) return
        notifications.createNotificationChannel(
            NotificationChannel(CHANNEL, context.getString(R.string.xfer_channel), NotificationManager.IMPORTANCE_LOW),
        )
        val builder =
            Notification
                .Builder(context, CHANNEL)
                .setSmallIcon(R.drawable.ic_laptop)
                .setContentTitle(title)
                .setOnlyAlertOnce(true)
                .setOngoing(ongoing)
                .setAutoCancel(!ongoing)
        if (ongoing) {
            builder.setProgress(100, percent, false)
            cancel?.let {
                val icon = Icon.createWithResource(context, R.drawable.ic_laptop)
                val label = context.getString(R.string.xfer_cancel)
                builder.addAction(Notification.Action.Builder(icon, label, it).build())
            }
        }
        notifications.notify(TAG, NOTICE_ID, builder.build())
    }

    private companion object {
        const val CHANNEL = "xfer"
        const val TAG = "xfer"

        /** With a tag of its own, so the same id as another notice cannot collide. */
        const val NOTICE_ID = 5
    }
}
