package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Test

class PickerHintTest {
    private fun hint(
        bluetoothOn: Boolean = true,
        bluetoothPermitted: Boolean = true,
        localNetworkGranted: Boolean = true,
        usbCable: Boolean = false,
        usbUp: Boolean = false,
        anyDevices: Boolean = false,
    ) = pickerHint(bluetoothOn, bluetoothPermitted, localNetworkGranted, usbCable, usbUp, anyDevices)

    @Test
    fun missingLocalNetworkPermissionComesFirst() {
        assertEquals(
            PickerHint.LOCAL_NETWORK,
            hint(localNetworkGranted = false, usbCable = true, anyDevices = true, bluetoothOn = false),
        )
    }

    @Test
    fun aCableWithoutTetheringComesBeforeNothingFound() {
        assertEquals(PickerHint.USB_TETHER_OFF, hint(usbCable = true))
        assertEquals(PickerHint.USB_TETHER_OFF, hint(usbCable = true, anyDevices = true))
        assertEquals(PickerHint.NOTHING_FOUND, hint(usbCable = true, usbUp = true))
    }

    @Test
    fun bluetoothOffNeverHidesFoundLaptops() {
        assertEquals(PickerHint.NONE, hint(bluetoothOn = false, anyDevices = true))
        assertEquals(PickerHint.NONE, hint(bluetoothPermitted = false, anyDevices = true))
    }

    @Test
    fun bluetoothOffIsTheLastThingSuggestedWhenNothingIsFound() {
        assertEquals(PickerHint.BLUETOOTH_OFF, hint(bluetoothOn = false))
        assertEquals(PickerHint.BLUETOOTH_PERMISSION, hint(bluetoothPermitted = false))
        assertEquals(PickerHint.NOTHING_FOUND, hint())
    }
}
