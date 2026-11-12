package me.akshitbansal.edgepad.link

/** Which half of a protocol mismatch is the older one. The higher protocol number is the newer release. */
enum class OlderHalf { LAPTOP, PHONE, UNKNOWN }

/**
 * [laptopVersion] is the number the laptop's reply carried, as [LaptopLink.Closure.detail] holds it; [ownVersion] is
 * this app's. UNKNOWN when the laptop's number does not read or is somehow the same, so the message names neither.
 */
internal fun olderHalf(
    laptopVersion: String,
    ownVersion: Int,
): OlderHalf {
    val theirs = laptopVersion.toIntOrNull() ?: return OlderHalf.UNKNOWN
    return when {
        theirs < ownVersion -> OlderHalf.LAPTOP
        theirs > ownVersion -> OlderHalf.PHONE
        else -> OlderHalf.UNKNOWN
    }
}
