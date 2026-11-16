package me.akshitbansal.edgepad.link

/** What a tap on a Bluetooth row with no paired device behind it calls for. */
enum class BluetoothTapHint {
    /** Bluetooth is off: say so, and offer its settings. */
    NOT_READY_OFF,

    /** Bluetooth is on but this app may not use it: ask for the permission. */
    NOT_READY_PERMISSION,

    /** Bluetooth is on and allowed, so an empty paired list really means the laptop is not paired. */
    READY,
}

/** Off first: without the adapter on, the permission alone would connect nothing. */
internal fun bluetoothTapHint(
    adapterOn: Boolean,
    permitted: Boolean,
): BluetoothTapHint =
    when {
        !adapterOn -> BluetoothTapHint.NOT_READY_OFF
        !permitted -> BluetoothTapHint.NOT_READY_PERMISSION
        else -> BluetoothTapHint.READY
    }
