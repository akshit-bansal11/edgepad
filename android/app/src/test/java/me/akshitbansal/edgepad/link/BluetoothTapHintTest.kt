package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Test

class BluetoothTapHintTest {
    @Test
    fun bluetoothOffIsSaidWhateverThePermission() {
        assertEquals(BluetoothTapHint.NOT_READY_OFF, bluetoothTapHint(adapterOn = false, permitted = false))
        assertEquals(BluetoothTapHint.NOT_READY_OFF, bluetoothTapHint(adapterOn = false, permitted = true))
    }

    @Test
    fun bluetoothOnWithoutThePermissionAsksForIt() {
        assertEquals(BluetoothTapHint.NOT_READY_PERMISSION, bluetoothTapHint(adapterOn = true, permitted = false))
    }

    @Test
    fun bluetoothOnAndAllowedIsReady() {
        assertEquals(BluetoothTapHint.READY, bluetoothTapHint(adapterOn = true, permitted = true))
    }
}
