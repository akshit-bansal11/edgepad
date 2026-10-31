package me.akshitbansal.edgepad.link

/**
 * What one laptop's row in System settings says: [ACTIVE] is the laptop every screen draws and sends to, [CONNECTED]
 * one that is up and waiting to be made active, [CONNECTING] an attempt or a retry under way, [ASKING] one waiting on
 * the owner's PIN or pairing code, and [NOT_CONNECTED] anything else, remembered or failed.
 */
enum class DeviceState { ACTIVE, CONNECTED, CONNECTING, ASKING, NOT_CONNECTED }

/** What a tap on a row does, decided by its [DeviceState] and nothing else. */
enum class DeviceTap { NOTHING, SELECT, ANSWER, CONNECT }

/** [row]'s state, [selected] being the laptop the manager has selected. Only a connected one is ever active. */
internal fun deviceState(
    row: LaptopRow,
    selected: Laptop?,
): DeviceState {
    val active = selected != null && row.laptop === selected
    return when (row.status) {
        RowStatus.CONNECTED -> if (active) DeviceState.ACTIVE else DeviceState.CONNECTED
        RowStatus.ASKING -> DeviceState.ASKING
        RowStatus.CONNECTING, RowStatus.AWAITING, RowStatus.RECONNECTING -> DeviceState.CONNECTING
        RowStatus.NONE, RowStatus.REMEMBERED, RowStatus.FAILED -> DeviceState.NOT_CONNECTED
    }
}

/**
 * A connected laptop is made active, a waiting prompt is answered, a laptop with nothing going on is connected, and the
 * active laptop and one still connecting are left alone: a second tap must not restart an attempt in flight.
 */
internal fun DeviceState.tap(): DeviceTap =
    when (this) {
        DeviceState.ACTIVE -> DeviceTap.NOTHING
        DeviceState.CONNECTED -> DeviceTap.SELECT
        DeviceState.CONNECTING -> DeviceTap.NOTHING
        DeviceState.ASKING -> DeviceTap.ANSWER
        DeviceState.NOT_CONNECTED -> DeviceTap.CONNECT
    }
