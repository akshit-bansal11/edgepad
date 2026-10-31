package me.akshitbansal.edgepad.link

import java.net.InetAddress
import java.net.UnknownHostException

/**
 * A laptop this phone remembers across launches. [id] is the [Target.key] it was reached by: never the id a Bluetooth
 * laptop claims in TEXT DEVICE, which nothing proves. So a laptop used over Bluetooth and the network is two lines and
 * two rows. [kind] is the transport last used. [autoConnect] says it connects when the app opens: a connect turns it
 * on, Disconnect off.
 */
data class SavedLaptop(
    val id: String,
    val kind: Transport,
    val name: String,
    val btAddress: String = "",
    val hosts: List<String> = emptyList(),
    val port: Int = 0,
    val autoConnect: Boolean = true,
) {
    val keys: Set<String> get() = setOf(id, btAddress) - ""

    fun isIn(keys: Set<String>): Boolean = id in keys || btAddress in keys

    /** Where to reach it over the network, or null when it has no laptop id or no address to try. */
    fun networkTarget(): Target.Network? {
        val laptopId = id.takeIf { it.startsWith(NET_KEY) }?.let { parseHex(it.removePrefix(NET_KEY)) } ?: return null
        val addresses =
            try {
                // Only literals get here, so this never touches DNS: see [isLiteral].
                hosts.map(InetAddress::getByName)
            } catch (e: UnknownHostException) {
                emptyList()
            }
        return if (addresses.isEmpty()) null else Target.Network(laptopId, name, addresses, port)
    }
}

/**
 * One line a laptop: `id, kind, name, Bluetooth address, hosts, port, auto-connect` separated by tabs; hosts are
 * comma-separated.
 */
internal fun encodeSaved(list: List<SavedLaptop>): String =
    list.joinToString("\n") { laptop ->
        listOf(
            laptop.id,
            laptop.kind.name,
            laptop.name.replace(LINE_BREAKERS, " "),
            laptop.btAddress,
            laptop.hosts.joinToString(","),
            laptop.port.toString(),
            if (laptop.autoConnect) "1" else "0",
        ).joinToString("\t")
    }

/** A line that does not read is skipped: losing one laptop beats losing them all, or failing to open. */
internal fun decodeSaved(text: String): List<SavedLaptop> = text.lineSequence().mapNotNull(::decodeLine).toList()

private fun decodeLine(line: String): SavedLaptop? {
    val fields = line.split('\t')
    if (fields.size != SAVED_FIELDS || fields[0].isEmpty()) return null
    val kind = Transport.entries.firstOrNull { it.name == fields[1] } ?: return null
    val port = fields[5].toIntOrNull()?.takeIf { it in 0..MAX_PORT } ?: return null
    val hosts = fields[4].split(',').filter(::isLiteral)
    return SavedLaptop(fields[0], kind, fields[2], fields[3], hosts, port, fields[6] == "1")
}

/** An address [InetAddress.getByName] parses without a lookup, which on the main thread would throw. */
private fun isLiteral(host: String): Boolean =
    host.isNotEmpty() && (':' in host || host.all { it.isDigit() || it == '.' })

/**
 * Moves the single `laptop=AA:BB:..` of a build that had one laptop into [saved]. The caller writes the result and
 * clears the old key in one edit, so neither can happen without the other.
 */
internal fun migrateSaved(
    saved: String,
    legacy: String,
): String {
    val list = decodeSaved(saved)
    return if (list.any { it.btAddress == legacy }) {
        saved
    } else {
        encodeSaved(list + SavedLaptop(legacy, Transport.BLUETOOTH, legacy, btAddress = legacy))
    }
}

/**
 * [entry] added, or folded into every line that is the same laptop: what it does not know, they do, and a new name is
 * how a rename lands. [first] puts it at the front, the order laptops connect in, so the active one is selected again.
 */
internal fun rememberLaptop(
    list: List<SavedLaptop>,
    entry: SavedLaptop,
    first: Boolean,
): List<SavedLaptop> {
    val same = list.filter { it.isIn(entry.keys) }
    val at = list.indexOfFirst { it.isIn(entry.keys) }
    val merged =
        same.fold(entry) { into, old ->
            into.copy(
                id = if (into.id.startsWith(NET_KEY)) into.id else old.id,
                btAddress = into.btAddress.ifEmpty { old.btAddress },
                hosts = into.hosts.ifEmpty { old.hosts },
                port = if (into.port != 0) into.port else old.port,
            )
        }
    val rest = list - same.toSet()
    return when {
        first -> listOf(merged) + rest
        at < 0 -> rest + merged
        else -> rest.take(at) + merged + rest.drop(at)
    }
}

/** The active laptop first, so it is the one selected at the next launch. */
internal fun promoted(
    list: List<SavedLaptop>,
    keys: Set<String>,
): List<SavedLaptop> = list.sortedByDescending { it.isIn(keys) }

internal fun forgotten(
    list: List<SavedLaptop>,
    keys: Set<String>,
): List<SavedLaptop> = list.filterNot { it.isIn(keys) }

/** What one row of Devices says about its laptop. */
enum class RowStatus { NONE, REMEMBERED, CONNECTING, AWAITING, ASKING, RECONNECTING, CONNECTED, FAILED }

/**
 * One laptop in Devices, however many ways it was seen: remembered, in [LaptopConnections], bonded, found on Wi-Fi or
 * over USB. [keys] names all of them. [network] holds one entry per transport. Bluetooth and the network are separate
 * rows, as only a pinned id joins two sightings and Bluetooth has none.
 */
class LaptopRow(
    val keys: Set<String>,
    val name: String,
    val laptop: Laptop?,
    val saved: SavedLaptop?,
    val bluetooth: String?,
    val network: List<Found>,
) {
    /** Every transport this laptop can be connected over now, in [Transport] order. */
    val reach: List<Transport> =
        buildSet {
            if (bluetooth != null) add(Transport.BLUETOOTH)
            network.forEach { add(it.transport) }
            saved?.takeIf { it.hosts.isNotEmpty() }?.let { add(it.kind) }
            laptop?.let { add(it.kind) }
        }.sorted()

    /** The transport a tap uses: what it is on or was last on, while that is still there. */
    val via: Transport? = (laptop?.kind ?: saved?.kind)?.takeIf { it in reach } ?: reach.firstOrNull()

    /** The sub-line when nothing more telling is to be said. */
    val detail: String =
        network
            .firstOrNull()
            ?.hosts
            ?.firstOrNull()
            ?.hostAddress
            ?: saved?.hosts?.firstOrNull()
            ?: bluetooth
            ?: ""

    val status: RowStatus
        get() {
            val laptop = laptop
            val resting = if (saved?.autoConnect == true) RowStatus.REMEMBERED else RowStatus.NONE
            val trying = laptop?.link != null || laptop?.retryTimer != null
            return when {
                laptop == null -> resting
                laptop.connected -> RowStatus.CONNECTED
                laptop.asking != null -> RowStatus.ASKING
                trying && laptop.reconnecting -> RowStatus.RECONNECTING
                trying && laptop.awaiting -> RowStatus.AWAITING
                trying -> RowStatus.CONNECTING
                laptop.failed || laptop.reconnecting -> RowStatus.FAILED
                else -> resting
            }
        }
}

private class Draft {
    val keys = HashSet<String>()
    var laptop: Laptop? = null
    var saved: SavedLaptop? = null
    var bluetooth: String? = null
    var bondedName: String? = null
    val network = ArrayList<Found>()

    private val name: String
        get() =
            laptop?.state?.laptopName
                ?: laptop?.name
                ?: saved?.name?.takeUnless { it == saved?.btAddress }
                ?: bondedName
                ?: network.firstOrNull()?.name
                ?: saved?.name
                ?: ""

    fun row() = LaptopRow(keys, name, laptop, saved, bluetooth, network)
}

/**
 * Folds every way a laptop was seen into one row, by [Target.key]. [bonded] is address to name. Sorted by name.
 */
internal fun laptopRows(
    saved: List<SavedLaptop>,
    live: List<Laptop>,
    bonded: List<Pair<String, String>>,
    found: List<Found>,
): List<LaptopRow> {
    val drafts = ArrayList<Draft>()

    fun draft(keys: Set<String>): Draft {
        val row = drafts.firstOrNull { row -> keys.any(row.keys::contains) } ?: Draft().also(drafts::add)
        row.keys.addAll(keys)
        return row
    }

    saved.forEach { draft(it.keys).saved = it }
    live.forEach { draft(setOf(it.id)).laptop = it }
    for ((address, name) in bonded) {
        draft(setOf(address)).apply {
            bluetooth = address
            bondedName = name
        }
    }
    found.forEach { draft(setOf(NET_KEY + it.idHex)).network.add(it) }
    return drafts.map(Draft::row).sortedBy { it.name.lowercase() }
}

private val LINE_BREAKERS = Regex("[\t\r\n]")
private const val SAVED_FIELDS = 7
private const val MAX_PORT = 65_535
