package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.net.Sas
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.Pin
import me.akshitbansal.edgepad.protocol.TextKind
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

class LaptopConnectionsTest {
    private val scheduler = FakeScheduler()
    private val made = ArrayList<FakeLink>()
    private val log = Recorder()
    private val manager =
        LaptopConnections(
            { target, events -> FakeLink(target, events).also { made.add(it) } },
            scheduler,
            log,
        ).apply {
            foreground = true
            reconnect = true
        }

    private fun net(
        n: Int,
        pairAgain: Boolean = false,
    ) = Target.Network(ByteArray(16) { n.toByte() }, "Laptop $n", emptyList(), 47321, pairAgain)

    private fun id(n: Int) = net(n).key

    private fun laptop(n: Int) = manager.laptops.first { it.id == id(n) }

    private fun connect(
        n: Int,
        tapped: Boolean = false,
    ): FakeLink {
        manager.connect(net(n), "Laptop $n", Transport.WIFI, tapped)
        return made.last()
    }

    private fun up(n: Int): FakeLink = connect(n).also { it.accept() }

    private fun linksTo(n: Int) = made.count { it.target.key == id(n) }

    private fun rowOf(n: Int) =
        laptopRows(emptyList(), manager.laptops, emptyList(), emptyList()).first { id(n) in it.keys }

    @Test
    fun sendGoesOnlyToTheSelectedLink() {
        val a = up(1)
        val b = up(2)
        manager.send(Frame.Ping(7))
        assertEquals(listOf<Frame>(Frame.Ping(7)), a.sent)
        assertTrue(b.sent.isEmpty())
        manager.select(id(2))
        manager.send(Frame.Ping(8))
        assertEquals(1, a.sent.size)
        assertEquals(listOf<Frame>(Frame.Ping(8)), b.sent)
    }

    @Test
    fun sendToReachesThatLaptopWhicheverIsSelected() {
        val a = up(1)
        val b = up(2)
        manager.sendTo(id(2), Frame.Ping(7))
        assertTrue(a.sent.isEmpty())
        assertEquals(1, b.sent.size)
    }

    @Test
    fun theFirstLaptopIsSelectedAndALaterOneIsNot() {
        up(1)
        up(2)
        assertSame(laptop(1), manager.selected)
    }

    @Test
    fun losingANonSelectedLinkRetriesWithNoEventForTheSelected() {
        val a = up(1)
        val b = up(2)
        log.lines.clear()
        b.drop()
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS)
        assertEquals(2, linksTo(2))
        assertFalse(log.lines.any { it.endsWith(id(1)) })
        assertNull(a.closedWith)
        assertSame(laptop(1), manager.selected)
    }

    @Test
    fun selectingALostLaptopDoesNotDropTheOthers() {
        val a = up(1)
        val b = up(2)
        b.drop()
        manager.select(id(2))
        assertSame(laptop(2), manager.selected)
        assertNull(a.closedWith)
        assertTrue(a.connected)
    }

    @Test
    fun retryCapsAtMaxAttemptsPerLaptop() {
        val other = up(2)
        up(1).drop()
        repeat(LaptopConnections.MAX_ATTEMPTS * 2) {
            scheduler.advance(LaptopConnections.RETRY_DELAY_MS)
            val last = made.last()
            if (last !== other) last.drop(LaptopLink.Cause.UNREACHABLE)
        }
        assertEquals(LaptopConnections.MAX_ATTEMPTS, linksTo(1))
        assertEquals(LaptopConnections.MAX_ATTEMPTS, laptop(1).attempts)
        assertEquals(0, laptop(2).attempts)
    }

    @Test
    fun aLinkThatLastedStartsAFreshSetOfAttempts() {
        val a = up(1)
        scheduler.advance(LaptopConnections.STABLE_LINK_MS)
        a.drop()
        assertEquals(0, laptop(1).attempts)
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS)
        made.last().accept()
        made.last().drop()
        assertEquals(1, laptop(1).attempts)
    }

    @Test
    fun closeAllEndsEveryLinkWithEnded() {
        val a = up(1)
        val b = up(2)
        manager.closeAll(LaptopLink.Cause.ENDED)
        assertEquals(LaptopLink.Cause.ENDED, a.closedWith)
        assertEquals(LaptopLink.Cause.ENDED, b.closedWith)
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS * 2)
        assertEquals(2, made.size)
    }

    @Test
    fun aSelectionChangeFiresOneEventAndCancelsNothingElse() {
        val a = up(1)
        val b = up(2)
        up(3).drop()
        log.lines.clear()
        manager.select(id(2))
        manager.select(id(2))
        assertEquals(listOf("selected ${id(2)}"), log.lines)
        assertNull(a.closedWith)
        assertNull(b.closedWith)
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS)
        assertEquals(2, linksTo(3))
    }

    @Test
    fun anEventForAClosedLinkIsIgnored() {
        val old = connect(1)
        val next = connect(1)
        assertEquals(LaptopLink.Cause.ENDED, old.closedWith)
        log.lines.clear()
        old.events.onConnected()
        old.events.onFrame(Frame.Text(TextKind.NOW_PLAYING.id, "stale"))
        old.events.onRoundTrip(1.0)
        old.events.onClosed(LaptopLink.Closure(LaptopLink.Cause.LOST))
        assertTrue(log.lines.isEmpty())
        assertSame(next, laptop(1).link)
        assertEquals("", laptop(1).state.nowPlaying)
        assertTrue(laptop(1).rtt.median().isNaN())
    }

    @Test
    fun aFrameGoesToItsOwnLaptopsState() {
        up(1)
        val b = up(2)
        log.lines.clear()
        b.events.onFrame(Frame.Text(TextKind.NOW_PLAYING.id, "song"))
        b.events.onRoundTrip(4.0)
        assertEquals("song", laptop(2).state.nowPlaying)
        assertEquals("", laptop(1).state.nowPlaying)
        assertEquals(4.0, laptop(2).rtt.median(), 0.0)
        assertEquals(listOf("frame ${id(2)}", "rtt ${id(2)}"), log.lines)
    }

    @Test
    fun aFrameTheStateDoesNotKeepIsNoEvent() {
        val a = up(1)
        log.lines.clear()
        a.events.onFrame(Frame.Ping(1))
        assertTrue(log.lines.isEmpty())
    }

    @Test
    fun aTapThatNeverReachedTheLaptopGetsOneQuickRetry() {
        connect(1, tapped = true).drop(LaptopLink.Cause.UNREACHABLE)
        assertEquals(1, made.size)
        scheduler.advance(LaptopConnections.QUICK_RETRY_MS)
        assertEquals(2, made.size)
        made.last().drop(LaptopLink.Cause.UNREACHABLE)
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS * 2)
        assertEquals(2, made.size)
    }

    @Test
    fun anAutomaticConnectGetsNoQuickRetry() {
        connect(1).drop(LaptopLink.Cause.UNREACHABLE)
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS * 2)
        assertEquals(1, made.size)
    }

    @Test
    fun aRetryNeverRepeatsPairAgain() {
        manager.connect(net(1, pairAgain = true), "Laptop 1", Transport.WIFI)
        made.last().accept()
        made.last().drop()
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS)
        assertTrue((made[0].target as Target.Network).pairAgain)
        assertFalse((made[1].target as Target.Network).pairAgain)
    }

    @Test
    fun theSelectedLaptopIsPingedFastAndTheOthersSlowly() {
        val a = up(1)
        val b = up(2)
        manager.fastPing = true
        scheduler.advance(LaptopConnections.PING_SLOW_MS)
        assertEquals(11, a.sent.size)
        assertEquals(2, b.sent.size)
    }

    @Test
    fun aClosedLinkStopsBeingPinged() {
        val a = up(1)
        scheduler.advance(0L)
        manager.disconnect(id(1))
        val pings = a.sent.size
        scheduler.advance(LaptopConnections.PING_SLOW_MS * 2)
        assertEquals(pings, a.sent.size)
    }

    @Test
    fun anEventAfterARebuildReachesTheNewActivity() {
        val a = connect(1)
        log.lines.clear()
        val rebuilt = Recorder()
        manager.events = rebuilt
        a.accept()
        assertEquals(listOf("connected ${id(1)}"), rebuilt.lines)
        assertTrue(log.lines.isEmpty())
    }

    @Test
    fun forgettingTheSelectedMovesToTheNextConnected() {
        val a = up(1)
        val b = up(2)
        manager.forget(id(1))
        assertEquals(LaptopLink.Cause.ENDED, a.closedWith)
        assertEquals(1, manager.laptops.size)
        assertSame(laptop(2), manager.selected)
        manager.forget(id(2))
        assertEquals(LaptopLink.Cause.ENDED, b.closedWith)
        assertNull(manager.selected)
    }

    @Test
    fun forgettingALostLaptopCancelsItsRetry() {
        up(1)
        up(2).drop()
        manager.forget(id(2))
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS)
        assertEquals(1, linksTo(2))
    }

    @Test
    fun whatAForgottenLinkHadQueuedIsDroppedAndOnlyItsCloseArrives() {
        val attempt = connect(1)
        attempt.holdClose = true
        manager.forget(id(1))
        log.lines.clear()
        attempt.events.onConnected()
        attempt.events.onFrame(Frame.Text(TextKind.DEVICE.id, "${"01".repeat(16)}/windows/Desk"))
        assertTrue(log.lines.isEmpty())
        attempt.events.onClosed(LaptopLink.Closure(LaptopLink.Cause.ENDED))
        assertEquals(listOf("closed ${id(1)}"), log.lines)
    }

    @Test
    fun whatADisconnectedLinkHadQueuedIsDropped() {
        val link = up(1)
        link.holdClose = true
        manager.disconnect(id(1))
        log.lines.clear()
        link.events.onFrame(Frame.Text(TextKind.DEVICE.id, "${"01".repeat(16)}/windows/Desk"))
        link.events.onRoundTrip(1.0)
        assertTrue(log.lines.isEmpty())
    }

    @Test
    fun aLossQueuedBehindADisconnectIsNotRetried() {
        val link = up(1)
        link.holdClose = true
        link.drop()
        manager.disconnect(id(1))
        link.events.onClosed(LaptopLink.Closure(LaptopLink.Cause.LOST))
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS)
        assertEquals(1, made.size)
        assertFalse(laptop(1).failed)
    }

    @Test
    fun connectingAgainAfterADisconnectHearsTheNewLink() {
        up(1)
        manager.disconnect(id(1))
        log.lines.clear()
        up(1)
        assertTrue("connected ${id(1)}" in log.lines)
    }

    @Test
    fun disconnectKeepsTheLaptopAndEndsItsRetries() {
        up(1).drop()
        manager.disconnect(id(1))
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS)
        assertEquals(1, made.size)
        assertEquals(1, manager.laptops.size)
    }

    @Test
    fun leavingTheForegroundHoldsRetriesAndComingBackTriesAtOnce() {
        up(1).drop()
        manager.foreground = false
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS)
        assertEquals(1, made.size)
        manager.foreground = true
        scheduler.advance(0L)
        assertEquals(2, made.size)
    }

    @Test
    fun theReconnectSettingOffMeansNoRetry() {
        manager.reconnect = false
        up(1).drop()
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS)
        assertEquals(1, made.size)
    }

    @Test
    fun stoppingRetriesEndsTheAttemptInFlightWithNoEvent() {
        up(1).drop()
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS)
        val attempt = made.last()
        log.lines.clear()
        manager.stopRetrying(id(1))
        assertEquals(LaptopLink.Cause.ENDED, attempt.closedWith)
        assertTrue(log.lines.isEmpty())
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS)
        assertEquals(2, made.size)
    }

    @Test
    fun retryNowStartsOverAfterTheCap() {
        up(1).drop()
        laptop(1).attempts = LaptopConnections.MAX_ATTEMPTS
        manager.retryNow(id(1))
        assertEquals(0, laptop(1).attempts)
        assertEquals(2, made.size)
    }

    @Test
    fun threeLaptopsLosingOneNeverTouchesTheSelected() {
        val a = up(1)
        val b = up(2)
        val c = up(3)
        manager.select(id(2))
        log.lines.clear()
        a.drop()
        assertSame(laptop(2), manager.selected)
        assertTrue(laptop(1).reconnecting)
        assertTrue(b.connected)
        assertTrue(c.connected)
        assertEquals(listOf("closed ${id(1)}"), log.lines)
    }

    @Test
    fun forgettingTheActiveOfThreeMovesToTheNextConnectedThenToNone() {
        up(1).drop()
        manager.select(id(1))
        val b = up(2)
        up(3)
        manager.select(id(2))
        manager.forget(id(2))
        assertEquals(LaptopLink.Cause.ENDED, b.closedWith)
        assertSame(laptop(3), manager.selected)
        manager.forget(id(3))
        assertNull(manager.selected)
    }

    @Test
    fun theOldLaptopHearsReleasesBeforeTheSelectionMoves() {
        val a = up(1)
        val b = up(2)
        log.onDeselecting = { manager.send(Frame.Key(7, false)) }
        manager.select(id(2))
        assertEquals(listOf<Frame>(Frame.Key(7, false)), a.sent)
        assertTrue(b.sent.isEmpty())
        assertEquals(1, log.deselected)
    }

    @Test
    fun disconnectingTheSelectedMovesToTheNextConnected() {
        up(1)
        up(2)
        manager.disconnect(id(1))
        assertSame(laptop(2), manager.selected)
        assertEquals(2, manager.laptops.size)
    }

    @Test
    fun aDisconnectWithNoOtherKeepsTheSelection() {
        up(1)
        manager.disconnect(id(1))
        assertSame(laptop(1), manager.selected)
    }

    @Test
    fun aSelectedLaptopThatFailsForGoodGivesWayToAConnectedOne() {
        val first = connect(1)
        up(2)
        first.drop(LaptopLink.Cause.REFUSED)
        assertSame(laptop(2), manager.selected)
        assertTrue(laptop(1).failed)
    }

    @Test
    fun theFirstLaptopUpTakesOverFromASelectedOneThatGaveUp() {
        connect(1).drop(LaptopLink.Cause.UNREACHABLE)
        assertSame(laptop(1), manager.selected)
        up(2)
        assertSame(laptop(2), manager.selected)
    }

    @Test
    fun aLostSelectedLaptopKeepsTheSelectionWhileItRetries() {
        up(1).drop()
        up(2)
        assertSame(laptop(1), manager.selected)
    }

    @Test
    fun aTapThatAsksForTheSelectionTakesIt() {
        up(1)
        manager.connect(net(2), "Laptop 2", Transport.WIFI, tapped = true, select = true)
        assertSame(laptop(2), manager.selected)
    }

    @Test
    fun aSecondTransportLandsOnTheSameRecordAndReplacesItsLink() {
        val wifi = up(1)
        manager.connect(net(1), "Laptop 1", Transport.USB)
        assertEquals(1, manager.laptops.size)
        assertEquals(LaptopLink.Cause.ENDED, wifi.closedWith)
        assertEquals(Transport.USB, laptop(1).kind)
    }

    @Test
    fun aLaptopClaimingAnotherLaptopsIdChangesNothing() {
        val wifi = up(1)
        // Stands in for a Bluetooth link, which no unit test can build: nothing here has proven the id it names.
        val claimant = up(2)
        log.lines.clear()
        claimant.events.onFrame(Frame.Text(TextKind.DEVICE.id, "${"01".repeat(16)}/windows/Desk"))
        assertTrue(wifi.connected)
        assertTrue(claimant.connected)
        assertNull(wifi.closedWith)
        assertEquals(2, manager.laptops.size)
        assertSame(laptop(1), manager.selected)
        assertEquals(listOf("frame ${id(2)}"), log.lines)
        assertEquals(2, laptopRows(emptyList(), manager.laptops, emptyList(), emptyList()).size)
    }

    @Test
    fun aPinFromALaptopNotSelectedWaitsOnItsRowAndATimeoutFailsOnlyThatLaptop() {
        val a = up(1)
        val pin = connect(2)
        pin.events.onPinRequest(3)
        assertEquals(Laptop.Ask.PIN, laptop(2).asking)
        assertEquals(RowStatus.ASKING, rowOf(2).status)
        assertSame(laptop(1), manager.selected)
        pin.drop(LaptopLink.Cause.NO_ANSWER)
        assertNull(laptop(2).asking)
        assertEquals(RowStatus.FAILED, rowOf(2).status)
        assertTrue(a.connected)
        assertSame(laptop(1), manager.selected)
    }

    @Test
    fun aPairingCodeWaitsOnItsLaptopWithItsOwnDeadline() {
        up(1)
        val pairing = connect(2)
        val code = Sas("123456")
        pairing.events.onPairingCode(code)
        assertSame(code, laptop(2).pairCode)
        assertEquals(LaptopConnections.PAIR_CODE_MS, laptop(2).pairDeadline)
        assertEquals(Laptop.Ask.PAIR_CODE, laptop(2).asking)
        pairing.events.onPairingConfirm()
        assertTrue(laptop(2).pairAsking)
        pairing.accept()
        assertNull(laptop(2).asking)
        assertFalse(laptop(2).pairAsking)
    }

    @Test
    fun aRowFollowsItsLaptop() {
        up(1)
        val b = connect(2)
        assertEquals(RowStatus.CONNECTED, rowOf(1).status)
        assertEquals(RowStatus.CONNECTING, rowOf(2).status)
        b.events.onAwaitingLaptop()
        assertEquals(RowStatus.AWAITING, rowOf(2).status)
        b.accept()
        b.drop()
        assertEquals(RowStatus.RECONNECTING, rowOf(2).status)
    }

    @Test
    fun shutDownClosesEverythingWithNoEvent() {
        val a = up(1)
        val b = up(2)
        log.lines.clear()
        manager.shutDown()
        assertEquals(LaptopLink.Cause.ENDED, a.closedWith)
        assertEquals(LaptopLink.Cause.ENDED, b.closedWith)
        assertTrue(log.lines.isEmpty())
        assertTrue(manager.laptops.isEmpty())
        assertNull(manager.selected)
    }

    @Test
    fun aSelectionChangeSpeedsUpTheNewLaptopsPings() {
        up(1)
        val b = up(2)
        manager.fastPing = true
        scheduler.advance(0L)
        val before = b.sent.size
        manager.select(id(2))
        scheduler.advance(LaptopConnections.PING_FAST_MS * 2)
        assertEquals(before + 3, b.sent.size)
    }

    @Test
    fun aPinQuestionDuringARetryEndsTheRetrying() {
        up(1).drop()
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS)
        val attempt = made.last()
        attempt.events.onPinRequest(3)
        assertEquals(3, laptop(1).pinAttemptsLeft)
        attempt.drop(LaptopLink.Cause.REFUSED)
        scheduler.advance(LaptopConnections.RETRY_DELAY_MS)
        assertEquals(2, made.size)
    }

    private class FakeLink(
        val target: Target,
        val events: LinkEvents,
    ) : ManagedLink {
        val sent = ArrayList<Frame>()
        var closedWith: LaptopLink.Cause? = null

        /** A real link posts its close: a test sets this and delivers what was queued by hand, then the close. */
        var holdClose = false
        override val id: Any = Any()
        override var connected = false
        override var wasConnected = false
        override var iconPx = 128
        override var blobs: ((Frame) -> Unit)? = null
        override val queued = 0
        override val lastInputMs = 0L

        fun accept() {
            connected = true
            wasConnected = true
            events.onConnected()
        }

        fun drop(cause: LaptopLink.Cause = LaptopLink.Cause.LOST) = close(LaptopLink.Closure(cause))

        override fun start() = Unit

        override fun send(frame: Frame) {
            sent.add(frame)
        }

        override fun answerPin(pin: Pin) = Unit

        override fun answerPairing(accepted: Boolean) = Unit

        override fun close(closure: LaptopLink.Closure) {
            if (closedWith != null) return
            closedWith = closure.cause
            connected = false
            if (!holdClose) events.onClosed(closure)
        }
    }

    private class Recorder : LaptopConnections.Events {
        val lines = ArrayList<String>()
        var onDeselecting: () -> Unit = {}
        var deselected = 0

        override fun onConnecting(laptop: Laptop) {
            lines.add("connecting ${laptop.id}")
        }

        override fun onConnected(laptop: Laptop) {
            lines.add("connected ${laptop.id}")
        }

        override fun onAwaitingLaptop(laptop: Laptop) {
            lines.add("awaiting ${laptop.id}")
        }

        override fun onPinRequest(laptop: Laptop) {
            lines.add("pin ${laptop.id}")
        }

        override fun onPairingCode(
            laptop: Laptop,
            code: Sas,
        ) {
            lines.add("code ${laptop.id}")
        }

        override fun onPairingConfirm(laptop: Laptop) {
            lines.add("confirm ${laptop.id}")
        }

        override fun onFrame(
            laptop: Laptop,
            frame: Frame,
        ) {
            lines.add("frame ${laptop.id}")
        }

        override fun onRoundTrip(
            laptop: Laptop,
            ms: Double,
        ) {
            lines.add("rtt ${laptop.id}")
        }

        override fun onClosed(
            laptop: Laptop,
            closure: LaptopLink.Closure,
        ) {
            lines.add("closed ${laptop.id}")
        }

        override fun onDeselecting(laptop: Laptop) {
            deselected++
            onDeselecting()
        }

        override fun onSelected(laptop: Laptop?) {
            lines.add("selected ${laptop?.id}")
        }
    }

    private class FakeScheduler : Scheduler {
        private class Job(
            val at: Long,
            val run: () -> Unit,
        )

        private val jobs = ArrayList<Job>()
        private var time = 0L

        override fun now(): Long = time

        override fun after(
            delayMs: Long,
            run: () -> Unit,
        ): () -> Unit {
            val job = Job(time + delayMs, run)
            jobs.add(job)
            return { jobs.remove(job) }
        }

        fun advance(ms: Long) {
            val end = time + ms
            while (true) {
                val next = jobs.filter { it.at <= end }.minByOrNull { it.at } ?: break
                jobs.remove(next)
                time = next.at
                next.run()
            }
            time = end
        }
    }
}
