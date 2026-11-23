package me.akshitbansal.edgepad.link.xfer

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.pm.PackageManager
import android.graphics.drawable.Icon
import android.text.format.Formatter
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
 * once per percent, with Pause (or Resume) and Stop actions; a tap on it opens the app, which shows the transfer card
 * again. Never without permission. No timer; the final state replaces the bar.
 *
 * @param cancel What the Stop action fires; the wiring supplies it. Without one the action is left off.
 * @param pause What the Pause or Resume action fires. Without one the action is left off.
 */
class XferNotification(
    context: Context,
    private val cancel: PendingIntent? = null,
    private val pause: PendingIntent? = null,
) {
    private val context: Context = context.applicationContext
    private var gate = PercentGate()
    private var gateIndex = 0
    private var paused = false
    private var lastTitle: String? = null
    private var lastDone = 0L
    private var lastTotal = 0L

    /**
     * Shows [done] of [total] bytes of [name] if that is a new percent; [name] is file [index] of [count] in a batch
     * sent. Safe on any thread, one transfer at a time.
     */
    fun progress(
        name: String,
        done: Long,
        total: Long,
        sending: Boolean,
        index: Int = 1,
        count: Int = 1,
    ) {
        if (index != gateIndex) {
            gate = PercentGate()
            gateIndex = index
        }
        if (!gate.pass(done, total)) return
        val percent = if (total <= 0) 100 else (done * 100 / total).toInt()
        val shown = middleTruncated(name)
        val title =
            when {
                sending && count > 1 -> context.getString(R.string.xfer_sending_n, shown, index, count, percent)
                sending -> context.getString(R.string.xfer_sending, shown, percent)
                else -> context.getString(R.string.xfer_receiving, shown, percent)
            }
        lastTitle = title
        lastDone = done
        lastTotal = total
        post(title, ongoing = true, percent = percent)
    }

    /** The transfer was held or let go: rebuilds the notification now, so its action and text follow. No timer. */
    fun held(held: Boolean) {
        if (held == paused) return
        paused = held
        val title = lastTitle ?: return
        val percent = if (lastTotal <= 0) 100 else (lastDone * 100 / lastTotal).toInt()
        post(title, ongoing = true, percent = percent)
    }

    /** Replaces the bar with the outcome of a batch of several files: all [sent], or how many [failed]. */
    fun finishBatch(
        sent: Int,
        failed: Int,
    ) {
        val title =
            if (failed == 0) {
                context.resources.getQuantityString(R.plurals.xfer_card_sent, sent, sent)
            } else {
                context.resources.getQuantityString(R.plurals.xfer_card_not_sent, failed, failed)
            }
        post(title, ongoing = false)
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
        // The end of a batch lets go of any hold.
        if (!ongoing) paused = false
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
        context.packageManager.getLaunchIntentForPackage(context.packageName)?.let {
            val open = PendingIntent.getActivity(context, 0, it, PendingIntent.FLAG_IMMUTABLE)
            builder.setContentIntent(open)
        }
        if (ongoing) {
            builder.setProgress(100, percent, false)
            if (paused) {
                val sent = Formatter.formatFileSize(context, lastDone)
                val size = Formatter.formatFileSize(context, lastTotal)
                builder.setContentText(context.getString(R.string.xfer_detail_paused, sent, size))
            }
            pause?.let {
                val icon = Icon.createWithResource(context, if (paused) R.drawable.ic_play else R.drawable.ic_pause)
                val label = context.getString(if (paused) R.string.xfer_resume else R.string.xfer_pause)
                builder.addAction(Notification.Action.Builder(icon, label, it).build())
            }
            cancel?.let {
                val icon = Icon.createWithResource(context, R.drawable.ic_stop)
                val label = context.getString(R.string.xfer_stop)
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
