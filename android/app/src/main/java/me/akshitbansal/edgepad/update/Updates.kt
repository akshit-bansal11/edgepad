package me.akshitbansal.edgepad.update

import java.io.IOException
import java.net.HttpURLConnection
import java.net.URI

/**
 * Whether GitHub holds a newer release than the one running.
 *
 * Asked without the API: the releases/latest page answers with a redirect to the newest tag, so one HEAD
 * request and its Location header say everything, with no JSON to read and no API rate limit to meet. It is
 * the only thing this app ever says to the internet, and it sends nothing about the phone.
 */
object Updates {
    private const val RELEASES = "https://github.com/akshit-bansal11/edgepad/releases"
    private const val LATEST_URL = "$RELEASES/latest"
    const val CHECK_EVERY_MS = 24 * 60 * 60 * 1000L
    private const val TAG_URL = "$RELEASES/tag/v"
    private const val TIMEOUT_MS = 10_000

    private val version = Regex("""\d{1,4}\.\d{1,4}\.\d{1,4}""")

    /** The newest release's version, such as "3.2.1"; null when GitHub could not be asked or gave no answer. */
    fun latest(): String? =
        try {
            val connection = URI(LATEST_URL).toURL().openConnection()
            if (connection is HttpURLConnection) {
                try {
                    connection.instanceFollowRedirects = false
                    connection.requestMethod = "HEAD"
                    connection.connectTimeout = TIMEOUT_MS
                    connection.readTimeout = TIMEOUT_MS
                    versionIn(connection.getHeaderField("Location"))
                } finally {
                    connection.disconnect()
                }
            } else {
                null
            }
        } catch (e: IOException) {
            null
        }

    /**
     * The version a redirect [location] names. Anything but this repository's own tag page with a plain
     * three-part version is refused: the answer is shown on screen and later builds a download address, and
     * it came from the network.
     */
    fun versionIn(location: String?): String? =
        location?.takeIf { it.startsWith(TAG_URL) }?.removePrefix(TAG_URL)?.takeIf(version::matches)

    /** Where a release keeps its APK. [version] is one [versionIn] accepted, so it adds nothing to the path. */
    fun apkUrl(version: String): String = "$RELEASES/download/v$version/Edgepad-$version.apk"

    /**
     * Whether [candidate] is a later release than [running]. A suffix such as "-draft.38" or "-dev" is
     * ignored, and a version that cannot be read counts as not newer, so a bad answer never offers an update.
     */
    fun isNewer(
        candidate: String,
        running: String,
    ): Boolean {
        val theirs = parts(candidate) ?: return false
        val ours = parts(running) ?: return false
        for (i in 0 until maxOf(theirs.size, ours.size)) {
            val difference = theirs.getOrElse(i) { 0 } - ours.getOrElse(i) { 0 }
            if (difference != 0) return difference > 0
        }
        return false
    }

    private fun parts(value: String): List<Int>? {
        val numbers = value.substringBefore('-').split('.').map { it.toIntOrNull() }
        return numbers.filterNotNull().takeIf { it.size == numbers.size }
    }
}
