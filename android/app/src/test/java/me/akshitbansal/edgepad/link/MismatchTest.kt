package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Test

class MismatchTest {
    @Test
    fun aLowerLaptopNumberMeansTheLaptopIsOlder() {
        assertEquals(OlderHalf.LAPTOP, olderHalf("4", 5))
    }

    @Test
    fun aHigherLaptopNumberMeansThePhoneIsOlder() {
        assertEquals(OlderHalf.PHONE, olderHalf("6", 5))
    }

    @Test
    fun anUnreadableOrEqualNumberNamesNeitherHalf() {
        assertEquals(OlderHalf.UNKNOWN, olderHalf("", 5))
        assertEquals(OlderHalf.UNKNOWN, olderHalf("five", 5))
        assertEquals(OlderHalf.UNKNOWN, olderHalf("5", 5))
    }
}
