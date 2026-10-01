package me.akshitbansal.edgepad.update

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import android.os.Build
import android.util.Log
import android.widget.Toast
import me.akshitbansal.edgepad.R
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URI

/**
 * Downloads a release's APK and hands it to the system to install over this app.
 *
 * The download is not checked against a hash here, because the system does a stronger check of its own: an
 * update is installed only if it is signed with the key the installed app was signed with, and is not an
 * older version. A download that was swapped or damaged on the way is refused there, whatever it claims.
 */
object Installer {
    private const val TAG = "Edgepad"
    private const val TIMEOUT_MS = 15_000
    private const val BUFFER_BYTES = 64 * 1024
    private const val PERCENT = 100

    /** Many times the size of any release so far. A response larger than this is not an Edgepad APK. */
    private const val MAX_BYTES = 64L * 1024 * 1024

    /**
     * Blocks while [version]'s APK is streamed into an install session, then commits it. What the system
     * decides is reported to [StatusReceiver]. False when the download or the session failed, with nothing
     * left behind. [onProgress] is called off the main thread with a percentage, when the size is known.
     */
    fun install(
        context: Context,
        version: String,
        onProgress: (Int) -> Unit,
    ): Boolean {
        val installer = context.packageManager.packageInstaller
        val params =
            PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
                setAppPackageName(context.packageName)
                // An app updating itself can be spared the system's "update this app?" question. Whether
                // it is spared is the system's decision; when it is not, StatusReceiver shows the question.
                setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED)
            }
        return try {
            val id = installer.createSession(params)
            val session = installer.openSession(id)
            try {
                download(Updates.apkUrl(version), session, onProgress)
                val status =
                    PendingIntent.getBroadcast(
                        context,
                        id,
                        Intent(context, StatusReceiver::class.java),
                        PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_MUTABLE,
                    )
                session.commit(status.intentSender)
                true
            } catch (e: IOException) {
                session.abandon()
                throw e
            } finally {
                session.close()
            }
        } catch (e: IOException) {
            Log.w(TAG, "The update could not be downloaded", e)
            false
        } catch (e: SecurityException) {
            Log.w(TAG, "The system refused the install session", e)
            false
        }
    }

    private fun download(
        url: String,
        session: PackageInstaller.Session,
        onProgress: (Int) -> Unit,
    ) {
        val connection = URI(url).toURL().openConnection()
        if (connection !is HttpURLConnection) throw IOException("Not an HTTP address: $url")
        try {
            connection.connectTimeout = TIMEOUT_MS
            connection.readTimeout = TIMEOUT_MS
            if (connection.responseCode != HttpURLConnection.HTTP_OK) {
                throw IOException("GitHub answered ${connection.responseCode} for $url")
            }
            val total = connection.contentLengthLong
            if (total > MAX_BYTES) throw IOException("$url is $total bytes, which is no Edgepad release")
            connection.inputStream.use { input ->
                session.openWrite("edgepad.apk", 0, total).use { output ->
                    val buffer = ByteArray(BUFFER_BYTES)
                    var written = 0L
                    var shown = -1
                    while (true) {
                        val read = input.read(buffer)
                        if (read < 0) break
                        written += read
                        // The size the server declared is not trusted to be the size it sends.
                        if (written > MAX_BYTES) throw IOException("$url sent more than $MAX_BYTES bytes")
                        output.write(buffer, 0, read)
                        if (total > 0) {
                            val percent = (written * PERCENT / total).toInt()
                            if (percent != shown) {
                                shown = percent
                                onProgress(percent)
                            }
                        }
                    }
                    session.fsync(output)
                }
            }
        } finally {
            connection.disconnect()
        }
    }

    /**
     * Hears what the system made of a committed session. Declared in the manifest as not exported, so only
     * the system and this app can reach it: it starts whatever activity the broadcast carries, and that must
     * never be an intent another app chose.
     */
    class StatusReceiver : BroadcastReceiver() {
        override fun onReceive(
            context: Context,
            intent: Intent,
        ) {
            when (val status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)) {
                PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                    confirmation(intent)?.let { context.startActivity(it.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
                }

                // Success replaces the app before anything could be shown, and a cancelled question is the
                // user changing their mind.
                PackageInstaller.STATUS_SUCCESS, PackageInstaller.STATUS_FAILURE_ABORTED -> {
                    Unit
                }

                else -> {
                    val message = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE)
                    Log.w(TAG, "The update was not installed: status $status, $message")
                    Toast.makeText(context, R.string.updates_install_failed, Toast.LENGTH_LONG).show()
                }
            }
        }

        /** The system's own "update this app?" screen, which it hands over when it wants the user asked. */
        private fun confirmation(intent: Intent): Intent? =
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                intent.getParcelableExtra(Intent.EXTRA_INTENT, Intent::class.java)
            } else {
                // NOTE: the typed overload arrived in Android 13 and the app runs on 12.
                @Suppress("DEPRECATION")
                intent.getParcelableExtra(Intent.EXTRA_INTENT)
            }
    }
}
