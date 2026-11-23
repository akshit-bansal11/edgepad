package me.akshitbansal.edgepad.link

/**
 * The approvals the phone still has to answer, oldest first, at most [MAX_WAITING]. Pure Kotlin, so a JVM test runs
 * it. An id is shown once: a repeat is ignored (the first text stays, so nothing changes under a finger), a fifth ask
 * is dropped (the laptop still asks in its own terminal), and an answered id is never queued again while the laptop
 * still lists it.
 */
class PermQueue {
    private val waiting = ArrayList<PermAsk>(MAX_WAITING)
    private val answered = LinkedHashSet<String>()

    /** The asks waiting, oldest first. A copy. */
    val asks: List<PermAsk> get() = waiting.toList()

    /** The ask to show now, or null. */
    val current: PermAsk? get() = waiting.firstOrNull()

    /** Queues [ask]; false when its id is waiting or answered already, or the queue is full. */
    fun add(ask: PermAsk): Boolean {
        if (ask.id in answered || waiting.any { it.id == ask.id } || waiting.size >= MAX_WAITING) return false
        waiting.add(ask)
        return true
    }

    /** PERM_GONE: the laptop no longer waits for [id]. True when it was waiting. */
    fun gone(id: String): Boolean = waiting.removeAll { it.id == id }

    /** The one PERM_ANSWER text for [id]; null when it is not waiting, so a second tap sends nothing. */
    fun answer(
        id: String,
        allow: Boolean,
    ): String? {
        if (!waiting.removeAll { it.id == id }) return null
        answered.add(id)
        if (answered.size > MAX_ANSWERED) answered.remove(answered.first())
        return PermAnswer.text(id, allow)
    }

    /**
     * Matches the queue to the laptop's list [latest] ([LaptopState.permAsks]): asks it dropped go, new ones join in
     * its order, and answered ids it no longer lists are forgotten.
     */
    fun sync(latest: List<PermAsk>) {
        waiting.removeAll { ask -> latest.none { it.id == ask.id } }
        answered.retainAll(latest.map { it.id }.toSet())
        latest.forEach { add(it) }
    }

    fun clear() {
        waiting.clear()
        answered.clear()
    }

    companion object {
        const val MAX_WAITING = 4
        private const val MAX_ANSWERED = 16
    }
}

/**
 * Whether Allow may be tapped: never for an ask whose text was over the cap ([complete] false), and only once the whole
 * text has been seen, because it [fits] the box or the box was scrolled to its end ([atEnd]).
 */
fun allowEnabled(
    complete: Boolean,
    fits: Boolean,
    atEnd: Boolean,
): Boolean = complete && (fits || atEnd)
