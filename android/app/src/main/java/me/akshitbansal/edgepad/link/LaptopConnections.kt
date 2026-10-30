package me.akshitbansal.edgepad.link

import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import me.akshitbansal.edgepad.link.net.Sas
import me.akshitbansal.edgepad.protocol.Frame

/** Time and delayed work, apart so the manager's tests can wind the clock by hand. */
interface Scheduler {
    fun now(): Long

    /** Runs [run] on the main thread after [delayMs]; the function returned cancels it. */
    fun after(
        delayMs: Long,
        run: () -> Unit,
    ): () -> Unit
}

class MainScheduler : Scheduler {
    private val handler = Handler(Looper.getMainLooper())

    override fun now(): Long = SystemClock.elapsedRealtime()

    override fun after(
        delayMs: Long,
        run: () -> Unit,
    ): () -> Unit {
        val job = Runnable(run)
        handler.postDelayed(job, delayMs)
        return { handler.removeCallbacks(job) }
    }
}

/**
 * Every laptop this phone is connected to or trying to reach. [selected] gets [send] and is the one the screens draw;
 * the others stay connected, are pinged slowly and retry on their own when lost.
 *
 * Main thread only. It outlives a rebuilt activity: the new one sets [events], which is read when each event is
 * delivered, so nothing lands on the dead activity. The links already post to the main thread, so a rebuild is one
 * message and no event can fall between the two activities.
 *
 * The retry rules are the activity's, per laptop. Retrying needs [foreground] and [reconnect]; a quick retry needs
 * only [foreground].
 */
class LaptopConnections(
    private val links: LinkFactory,
    private val scheduler: Scheduler,
    var events: Events,
) {
    /** A [LinkEvents] with the laptop named, plus what the manager itself decides. */
    interface Events {
        /** An attempt began, whether a [connect] or a retry. */
        fun onConnecting(laptop: Laptop)

        fun onConnected(laptop: Laptop)

        fun onAwaitingLaptop(laptop: Laptop)

        /** [Laptop.pinAttemptsLeft] holds the count. */
        fun onPinRequest(laptop: Laptop)

        fun onPairingCode(
            laptop: Laptop,
            code: Sas,
        )

        fun onPairingConfirm(laptop: Laptop)

        /** Only a frame [Laptop.state] kept; the state is already updated. */
        fun onFrame(
            laptop: Laptop,
            frame: Frame,
        )

        /** [Laptop.rtt] already holds [ms]. */
        fun onRoundTrip(
            laptop: Laptop,
            ms: Double,
        )

        /** Any laptop's link; [selected] says whether it was the one on screen. */
        fun onClosed(
            laptop: Laptop,
            closure: LaptopLink.Closure,
        )

        /**
         * [laptop] is about to stop being [selected], and still is: the last moment a release can reach it. Every
         * change of selection comes through here first.
         */
        fun onDeselecting(laptop: Laptop)

        /** [selected] changed. Null when no laptop is left to select. */
        fun onSelected(laptop: Laptop?)
    }

    private val all = ArrayList<Laptop>()

    val laptops: List<Laptop> get() = all

    var selected: Laptop? = null
        private set

    /** The activity is started. Off, waiting attempts are dropped; back on, every lost laptop tries at once. */
    var foreground = false
        set(value) {
            field = value
            refreshRetries()
        }

    /** The "Reconnect" setting. */
    var reconnect = false
        set(value) {
            field = value
            refreshRetries()
        }

    /** A screen showing the round trip is up: [selected] is pinged every [PING_FAST_MS], not [PING_SLOW_MS]. */
    var fastPing = false
        set(value) {
            val rising = value && !field
            field = value
            // The tick waiting may be a slow one.
            if (rising) selected?.let(::startPinger)
        }

    /**
     * Connects to [target], ending any link that laptop already had; other laptops are left alone. Selects it when
     * nothing is selected or [select] says so. [tapped] is the owner's own tap, which starts a fresh set of attempts
     * and earns the one quick retry; a retry or the automatic connect at start never does.
     *
     * Only [Target.key] finds a record. TEXT DEVICE is a claim, and over Bluetooth nothing proves it, so what a laptop
     * says of itself never ends a link, removes a record or moves the selection: a laptop reached over Bluetooth and
     * the network is two records. Wi-Fi and USB share a key because EDGL1 verifies that pinned id on every connect.
     */
    fun connect(
        target: Target,
        name: String,
        kind: Transport,
        tapped: Boolean = false,
        select: Boolean = false,
    ) {
        val laptop =
            all.firstOrNull { it.id == target.key }
                ?: Laptop(target.key, name, kind, target).also { all.add(it) }
        laptop.name = name
        laptop.kind = kind
        laptop.reconnecting = false
        if (tapped) laptop.attempts = 0
        if (selected == null || (select && laptop !== selected)) change(laptop)
        open(laptop, target)
        laptop.quickRetryLeft = tapped
    }

    /** Ends [id]'s link and its retries. The laptop stays listed; if it was [selected], the next connected one is. */
    fun disconnect(id: String) {
        val laptop = find(id) ?: return
        moveOn(laptop)
        end(laptop)
    }

    /** [disconnect], and off the list. If it was [selected], the next connected laptop is, or none. */
    fun forget(id: String) {
        val laptop = find(id) ?: return
        all.remove(laptop)
        if (selected === laptop) change(all.firstOrNull { it.connected })
        end(laptop)
    }

    /** Never touches a link or a retry: selecting a lost laptop leaves the others as they are. */
    fun select(id: String) {
        val laptop = find(id) ?: return
        if (laptop !== selected) change(laptop)
    }

    fun send(frame: Frame) {
        selected?.link?.send(frame)
    }

    fun sendTo(
        id: String,
        frame: Frame,
    ) {
        find(id)?.link?.send(frame)
    }

    /** The reconnect screen's "Try now". */
    fun retryNow(id: String) {
        val laptop = find(id) ?: return
        stopRetry(laptop)
        if (laptop.link != null) return
        if (laptop.attempts >= MAX_ATTEMPTS) laptop.attempts = 0
        if (foreground) open(laptop, laptop.target)
    }

    /** The reconnect screen left: no more attempts, and one in flight ends with no event. */
    fun stopRetrying(id: String) {
        val laptop = find(id) ?: return
        stopRetry(laptop)
        laptop.reconnecting = false
        val attempt = laptop.link
        if (attempt != null && !attempt.connected) {
            laptop.link = null
            attempt.close(LaptopLink.Closure(LaptopLink.Cause.ENDED))
        }
        moveOn(laptop)
    }

    /**
     * Closes every link with [cause], for the linger and for the app going away. A laptop already lost and waiting is
     * left as it is, so coming back still tries it.
     */
    fun closeAll(cause: LaptopLink.Cause) {
        all.forEach { it.link?.close(LaptopLink.Closure(cause)) }
    }

    /** The app is going away: every link closes and no event follows, for an activity that is no longer there. */
    fun shutDown() {
        for (laptop in all) {
            stopRetry(laptop)
            stopPinger(laptop)
            val link = laptop.link
            // Cleared first, as in [open]: this link's own close is then an event for a closed link.
            laptop.link = null
            link?.close(LaptopLink.Closure(LaptopLink.Cause.ENDED))
        }
        all.clear()
        selected = null
    }

    private fun find(id: String): Laptop? = all.firstOrNull { it.id == id }

    /** The only place [selected] moves. */
    private fun change(laptop: Laptop?) {
        selected?.let(events::onDeselecting)
        selected = laptop
        events.onSelected(laptop)
        // The tick waiting for this laptop may be a slow one.
        if (fastPing && laptop?.connected == true) startPinger(laptop)
    }

    /** [laptop] is done with for now: if the screens were on it, they go to the next connected laptop, if any. */
    private fun moveOn(laptop: Laptop) {
        if (laptop === selected) all.firstOrNull { it !== laptop && it.connected }?.let(::change)
    }

    private fun clearAsking(laptop: Laptop) {
        laptop.pinAsked = false
        laptop.pairCode = null
        laptop.pairAsking = false
        laptop.awaiting = false
    }


    private fun end(laptop: Laptop) {
        stopRetry(laptop)
        laptop.reconnecting = false
        laptop.link?.close(LaptopLink.Closure(LaptopLink.Cause.ENDED))
    }

    private fun open(
        laptop: Laptop,
        target: Target,
    ) {
        stopRetry(laptop)
        stopPinger(laptop)
        laptop.quickRetryLeft = false
        laptop.failed = false
        clearAsking(laptop)
        val old = laptop.link
        // Cleared first, so the old link's own close is an event for a closed link.
        laptop.link = null
        old?.close(LaptopLink.Closure(LaptopLink.Cause.ENDED))
        laptop.state.forgetFeatures()
        laptop.state.forgetMacroOpen()
        laptop.target = resumable(target)
        laptop.rtt.clear()
        val attempt = Attempt(laptop)
        val link = links.create(target, attempt)
        attempt.link = link
        laptop.link = link
        events.onConnecting(laptop)
        link.start()
    }

    /** A retry resumes: "Pair again" is the user's choice for one attempt, never something a retry repeats. */
    private fun resumable(target: Target): Target =
        if (target is Target.Network && target.pairAgain) {
            Target.Network(target.laptopId, target.name, target.hosts, target.port)
        } else {
            target
        }

    private fun closed(
        laptop: Laptop,
        link: ManagedLink,
        closure: LaptopLink.Closure,
    ) {
        stopPinger(laptop)
        laptop.link = null
        laptop.state.forgetMacroOpen()
        clearAsking(laptop)
        laptop.failed = closure.cause != LaptopLink.Cause.ENDED
        when {
            // A link ended on purpose starts the next one with a full set of attempts.
            closure.cause == LaptopLink.Cause.ENDED -> {
                laptop.attempts = 0
                laptop.reconnecting = false
            }

            // It was working and dropped on its own: try to get it back.
            link.wasConnected -> {
                val now = scheduler.now()
                laptop.lostAt = now
                if (now - laptop.connectedAt >= STABLE_LINK_MS) laptop.attempts = 0 else laptop.attempts++
                laptop.reconnecting = true
                scheduleRetry(laptop, RETRY_DELAY_MS)
            }

            laptop.reconnecting -> {
                laptop.attempts++
                scheduleRetry(laptop, RETRY_DELAY_MS)
            }

            // The first connect of a tap never reached the laptop: once more before saying anything.
            closure.cause == LaptopLink.Cause.UNREACHABLE && laptop.quickRetryLeft -> {
                laptop.quickRetryLeft = false
                attemptIn(laptop, QUICK_RETRY_MS)
            }
        }
        // Before the event, so a laptop that failed for good is reported as one no longer on screen.
        if (laptop.failed && laptop.idle) moveOn(laptop)
        events.onClosed(laptop, closure)
    }

    private fun refreshRetries() {
        for (laptop in all) {
            if (!foreground) stopRetry(laptop)
            if (laptop.reconnecting) scheduleRetry(laptop, 0L)
        }
    }

    private fun scheduleRetry(
        laptop: Laptop,
        delay: Long,
    ) {
        stopRetry(laptop)
        if (foreground && reconnect && laptop.attempts < MAX_ATTEMPTS) attemptIn(laptop, delay)
    }

    private fun attemptIn(
        laptop: Laptop,
        delay: Long,
    ) {
        laptop.retryTimer = scheduler.after(delay) { attempt(laptop) }
    }

    private fun attempt(laptop: Laptop) {
        if (foreground && laptop.link == null) open(laptop, laptop.target)
    }

    private fun stopRetry(laptop: Laptop) {
        laptop.retryTimer?.invoke()
        laptop.retryTimer = null
    }

    /** The only way a pinger starts: it clears any tick waiting, so one laptop can never have two running. */
    private fun startPinger(laptop: Laptop) {
        stopPinger(laptop)
        laptop.pingTimer = scheduler.after(0L) { ping(laptop) }
    }

    private fun stopPinger(laptop: Laptop) {
        laptop.pingTimer?.invoke()
        laptop.pingTimer = null
    }

    private fun ping(laptop: Laptop) {
        val link = laptop.link ?: return
        if (!link.connected) return
        link.send(Frame.Ping(System.nanoTime()))
        val interval = if (fastPing && laptop === selected) PING_FAST_MS else PING_SLOW_MS
        laptop.pingTimer = scheduler.after(interval) { ping(laptop) }
    }

    /** One link's reports. Anything from a link its laptop has moved on from is dropped here. */
    private inner class Attempt(
        private val laptop: Laptop,
    ) : LinkEvents {
        lateinit var link: ManagedLink

        private val current: Boolean get() = laptop.link === link

        override fun onConnected() {
            if (!current) return
            laptop.connectedAt = scheduler.now()
            laptop.reconnecting = false
            clearAsking(laptop)
            startPinger(laptop)
            // The selected one gave up or never answered: the first laptop to come up takes over.
            if (selected?.idle == true) change(laptop)
            events.onConnected(laptop)
        }

        override fun onAwaitingLaptop() {
            if (!current) return
            laptop.awaiting = true
            events.onAwaitingLaptop(laptop)
        }

        /** From here the owner is answering, so a failure is theirs to see and not one to retry. */
        override fun onPinRequest(attemptsLeft: Int) {
            if (!current) return
            laptop.pinAttemptsLeft = attemptsLeft
            laptop.pinAnswered = false
            laptop.pinAsked = true
            laptop.awaiting = false
            laptop.reconnecting = false
            events.onPinRequest(laptop)
        }

        override fun onPairingCode(code: Sas) {
            if (!current) return
            laptop.reconnecting = false
            laptop.awaiting = false
            laptop.pairCode = code
            laptop.pairDeadline = scheduler.now() + PAIR_CODE_MS
            laptop.pairAsking = false
            events.onPairingCode(laptop, code)
        }

        override fun onPairingConfirm() {
            if (!current) return
            laptop.pairAsking = true
            events.onPairingConfirm(laptop)
        }

        override fun onFrame(frame: Frame) {
            if (current && laptop.state.take(frame)) events.onFrame(laptop, frame)
        }

        override fun onRoundTrip(ms: Double) {
            if (!current) return
            laptop.rtt.add(ms)
            events.onRoundTrip(laptop, ms)
        }

        override fun onClosed(closure: LaptopLink.Closure) {
            if (current) closed(laptop, link, closure)
        }
    }

    companion object {
        const val MAX_ATTEMPTS = 10
        const val RETRY_DELAY_MS = 2_000L

        /** Long enough for the laptop to finish whatever made the first connect miss, short enough to go unnoticed. */
        const val QUICK_RETRY_MS = 1_500L

        /** A link that lasted less than this did not really come back, so it does not earn a fresh set of attempts. */
        const val STABLE_LINK_MS = 5_000L
        const val PING_FAST_MS = 500L
        const val PING_SLOW_MS = 5_000L

        /** The pairing code's countdown, the same 90 s the network transport waits for the laptop's answer. */
        const val PAIR_CODE_MS = 90_000L
    }
}
