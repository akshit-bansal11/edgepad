package me.akshitbansal.edgepad.link

/** The one thing the Devices list tells the user about, under the list. */
enum class PickerHint {
    /** Nothing to say. */
    NONE,

    /** Wi-Fi and USB laptops cannot be found without the local-network permission. */
    LOCAL_NETWORK,

    /** A cable is in and no USB tethering interface is up. */
    USB_TETHER_OFF,

    /** No laptops at all, and Bluetooth is on but the app may not use it. */
    BLUETOOTH_PERMISSION,

    /** No laptops at all and Bluetooth is off: the one thing left to suggest. */
    BLUETOOTH_OFF,

    /** No laptops at all, and nothing above explains it. */
    NOTHING_FOUND,
}

/**
 * What to tell the user, in precedence order. Bluetooth being off or unpermitted is reported only when no laptop is
 * listed, so it never hides laptops that were found on Wi-Fi or USB, nor the hints that explain those.
 */
internal fun pickerHint(
    bluetoothOn: Boolean,
    bluetoothPermitted: Boolean,
    localNetworkGranted: Boolean,
    usbCable: Boolean,
    usbUp: Boolean,
    anyDevices: Boolean,
): PickerHint =
    when {
        !localNetworkGranted -> PickerHint.LOCAL_NETWORK
        usbCable && !usbUp -> PickerHint.USB_TETHER_OFF
        anyDevices -> PickerHint.NONE
        !bluetoothOn -> PickerHint.BLUETOOTH_OFF
        !bluetoothPermitted -> PickerHint.BLUETOOTH_PERMISSION
        else -> PickerHint.NOTHING_FOUND
    }
