package me.akshitbansal.edgepad.update

/**
 * The one update download of this process. Kept here, not on the activity, so a rotation mid-download shows the
 * new activity the same percentage, and a second tap cannot start a second download and install session. Holds
 * no activity: whichever one is on screen sets [onChange] in onStart and clears it in onStop.
 */
object UpdateJob {
    /** How much has been downloaded, as a percentage; null when nothing is being downloaded. */
    @Volatile
    var progress: Int? = null

    /** Called on the main thread when [progress] changes or the download ends. */
    var onChange: (() -> Unit)? = null

    /** Claims the download. False while one is already running, and then nothing should start. */
    @Synchronized
    fun begin(): Boolean {
        if (progress != null) return false
        progress = 0
        return true
    }

    /** The download ended, however it ended: the next tap may start another. */
    @Synchronized
    fun finish() {
        progress = null
    }
}
