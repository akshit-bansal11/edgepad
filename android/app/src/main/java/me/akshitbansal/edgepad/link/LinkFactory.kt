package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.net.Sas
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.Pin
import kotlin.concurrent.thread

/**
 * What one link reports, on the main thread. [LaptopLink.Listener] with the link taken out: each attempt gets its
 * own [LinkEvents], so the receiver already knows which link is speaking, and a test can report without a real link.
 */
interface LinkEvents {
    fun onConnected()

    fun onAwaitingLaptop()

    fun onPinRequest(attemptsLeft: Int)

    fun onPairingCode(code: Sas)

    fun onPairingConfirm()

    fun onFrame(frame: Frame)

    fun onRoundTrip(ms: Double)

    fun onClosed(closure: LaptopLink.Closure)
}

/**
 * The part of a [LaptopLink] the manager touches. A seam of its own because a [LaptopLink] takes the main looper as
 * it is built, which a unit test does not have.
 */
interface ManagedLink {
    /** Tells this link from every other, which is how [CaptureInbox] binds its permits to one. */
    val id: Any
    val connected: Boolean
    val wasConnected: Boolean

    /** [LaptopLink.iconPx]. */
    var iconPx: Int

    /** Opens the link on a thread of its own and returns at once. */
    fun start()

    fun send(frame: Frame)

    fun answerPin(pin: Pin)

    fun answerPairing(accepted: Boolean)

    fun close(closure: LaptopLink.Closure)
}

/** Makes the link for a connect. Nothing reports before [ManagedLink.start]. */
fun interface LinkFactory {
    fun create(
        target: Target,
        events: LinkEvents,
    ): ManagedLink
}

/**
 * The real links. [transportFor] outlives the activity with the manager, so it must not hold one: application
 * context only.
 */
fun laptopLinks(transportFor: (Target) -> LinkTransport): LinkFactory =
    LinkFactory { target, events -> TransportLink(transportFor(target), events) }

private class TransportLink(
    transport: LinkTransport,
    private val events: LinkEvents,
) : ManagedLink,
    LaptopLink.Listener {
    private val laptopLink = LaptopLink(transport, this)

    override val id: Any get() = laptopLink.id
    override val connected: Boolean get() = laptopLink.connected
    override val wasConnected: Boolean get() = laptopLink.wasConnected
    override var iconPx: Int
        get() = laptopLink.iconPx
        set(value) {
            laptopLink.iconPx = value
        }

    override fun start() {
        thread(name = "edgepad-link") { laptopLink.open() }
    }

    override fun send(frame: Frame) = laptopLink.send(frame)

    override fun answerPin(pin: Pin) = laptopLink.answerPin(pin)

    override fun answerPairing(accepted: Boolean) = laptopLink.answerPairing(accepted)

    override fun close(closure: LaptopLink.Closure) = laptopLink.close(closure)

    override fun onConnected(link: LaptopLink) = events.onConnected()

    override fun onAwaitingLaptop(link: LaptopLink) = events.onAwaitingLaptop()

    override fun onPinRequest(
        link: LaptopLink,
        attemptsLeft: Int,
    ) = events.onPinRequest(attemptsLeft)

    override fun onPairingCode(
        link: LaptopLink,
        code: Sas,
    ) = events.onPairingCode(code)

    override fun onPairingConfirm(link: LaptopLink) = events.onPairingConfirm()

    override fun onFrame(frame: Frame) = events.onFrame(frame)

    override fun onRoundTrip(ms: Double) = events.onRoundTrip(ms)

    override fun onClosed(
        link: LaptopLink,
        closure: LaptopLink.Closure,
    ) = events.onClosed(closure)
}
