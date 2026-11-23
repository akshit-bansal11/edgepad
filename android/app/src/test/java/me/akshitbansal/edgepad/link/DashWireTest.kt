package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.AgentToken
import me.akshitbansal.edgepad.protocol.CiToken
import me.akshitbansal.edgepad.protocol.SessionStateToken
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** Runs protocol/dashboard-vectors.txt, which the C# and Swift suites read too, and the cases only this side has. */
class DashWireTest {
    @Test
    fun everyVectorHoldsOnThisSide() {
        var ran = 0
        for (line in vectorFile().readLines(Charsets.UTF_8)) {
            if (line.isBlank() || line.startsWith("#")) continue
            val space = line.indexOf(' ')
            val codec = line.substring(0, space)
            val cut = line.indexOf(ARROW)
            val args = line.substring(space + 1, cut)
            val expected = unescape(line.substring(cut + ARROW.length))
            val actual = run(codec, args, expected) ?: continue
            assertEquals(line, expected, actual)
            ran++
        }
        assertTrue("vectors ran: $ran", ran > 60)
    }

    @Test
    fun netInfoParsesTheFramesTxtVectors() {
        val wifi = DashWire.netInfo("wifi|5|866|72|192.168.1.23|192.168.1.1")
        assertEquals(NetInfo(NetKind.WIFI, NetBand.GHZ_5, 866, 72, "192.168.1.23", "192.168.1.1"), wifi)
        assertEquals(NetInfo(NetKind.NONE, NetBand.UNKNOWN, -1, -1, "", ""), DashWire.netInfo("none|?|-1|-1||"))
    }

    @Test
    fun netInfoToleratesUnknownTokensAndRefusesShapeErrors() {
        assertEquals(NetKind.OTHER, DashWire.netInfo("satellite|9|1|1||")?.kind)
        assertEquals(NetBand.UNKNOWN, DashWire.netInfo("wifi|9|1|1||")?.band)
        assertEquals(100, DashWire.netInfo("wifi|5|1|150||")?.signalPct)
        assertEquals("", DashWire.netInfo("wifi|5|1|1|not an ip|1.2.3.4")?.localIp)
        assertNull(DashWire.netInfo("wifi|5|1|1|"))
        assertNull(DashWire.netInfo("wifi|5|x|1||"))
    }

    @Test
    fun speedResultParsesTheFramesTxtVectors() {
        val done = DashWire.speedResult("done|93500|21400|11|2|29499900|31000000")
        assertEquals(SpeedResult(SpeedState.DONE, 93500, 21400, 11, 2, 29499900, 31000000), done)
        val running = DashWire.speedResult("running|-1|-1|-1|-1|29499900|0")
        assertEquals(SpeedResult(SpeedState.RUNNING, -1, -1, -1, -1, 29499900, 0), running)
    }

    @Test
    fun speedResultRefusesUnknownStateAndShapeErrorsAndCapsBytes() {
        assertNull(DashWire.speedResult("paused|1|1|1|1|1|1"))
        assertNull(DashWire.speedResult("done|1|1|1|1|1"))
        assertNull(DashWire.speedResult("done|a|1|1|1|1|1"))
        assertEquals(35_000_000L, DashWire.speedResult("capped|1|1|1|1|1|999999999999")?.bytes)
    }

    /** Null when the codec is an encoder only the laptop has. */
    private fun run(
        codec: String,
        args: String,
        expected: String,
    ): String? {
        val input = if (args == EMPTY) "" else unescape(args)
        return when (codec) {
            "CLEAN" -> {
                val sp = args.indexOf(' ')
                DashWire.clean(unescape(args.substring(sp + 1)), args.substring(0, sp).toInt()).marked()
            }

            "CLEANTEXT" -> {
                DashWire.cleanText(input).marked()
            }

            "STATSPARSE" -> {
                DashWire.stats(input)?.wire() ?: NONE
            }

            "STATSINFOPARSE" -> {
                DashWire.statsInfo(input)?.wire() ?: NONE
            }

            "DISKSPARSE" -> {
                DashWire.disks(input).joinToString("|") { it.wire() }.marked()
            }

            "SESSIONSPARSE" -> {
                DashWire.sessions(input).joinToString("|") { it.wire() }.marked()
            }

            "LIMITSPARSE" -> {
                DashWire.limits(input).joinToString("|") { it.wire() }.marked()
            }

            "SPENDPARSE" -> {
                DashWire.spend(input).joinToString("|") { it.wire() }.marked()
            }

            "WEATHERPARSE" -> {
                DashWire.weather(input)?.let { "${it.tempTenthsC}/${it.wmoCode}/${it.city}" } ?: NONE
            }

            "PERMLABELPARSE" -> {
                DashWire.permLabel(input)?.let { "${it.first} ${if (it.second) 1 else 0}" } ?: NONE
            }

            "PERMASKPARSE" -> {
                val ask = DashWire.permAsk("0123456789ab", true, input.toByteArray(Charsets.UTF_8))
                ask?.let { "${it.agent.token} @ ${it.tool} @ ${it.text.ifEmpty { EMPTY }}" } ?: NONE
            }

            "GITHUBPARSE" -> {
                DashWire.github(input.toByteArray(Charsets.UTF_8)).joinToString("\n") { it.wire() }.marked()
            }

            "PERMANSWER" -> {
                val allow = expected.startsWith("allow ")
                if (allow || expected.startsWith("deny ")) {
                    val id = expected.substringAfter(' ')
                    // what the phone sends is exactly what the laptop accepts
                    assertEquals(input, PermAnswer.text(id, allow))
                }
                null
            }

            else -> {
                null
            }
        }
    }

    private fun StatsSample.wire() =
        listOf(cpu, ram, gpu, vram, down, up, ping, loss, cpuTemp, gpuTemp, gpuPower).joinToString("/")

    private fun StatsInfo.wire() = "$cpuName|$ramMiB|$gpuName|$vramMiB|$netName"

    private fun DiskEntry.wire() = "$label:$usedPct:$totalGiB:$freeGiB"

    private fun SessionEntry.wire() = "${agent.token}:${state.token}:$contextPct:$title"

    private fun LimitEntry.wire() = "${agent.token}:${window.text}:$pct:$resetAtMin:$measuredAtMin"

    private fun SpendEntry.wire() =
        listOf(
            measuredAtMin,
            tokToday,
            centsToday,
            tokWeek,
            centsWeek,
            tokMonth,
            centsMonth,
            if (unpriced) 1 else 0,
        ).joinToString("/", prefix = agent.token + "/")

    private fun GithubRow.wire(): String {
        if (section == GithubSection.NOTIFICATIONS) return "N\t\t$number\t\t\tnone"
        val flags = if (draft) "d" else ""
        return "${section.char}\t$repo\t$number\t$title\t$flags\t${ci.token}"
    }

    private fun String.marked() = ifEmpty { EMPTY }

    @Test
    fun theRingWrapsAndKeepsOrder() {
        val ring = StatsRing()
        assertEquals(-1, ring.latest(StatsRing.CPU))
        for (i in 1..(StatsRing.CAPACITY + 5)) ring.push(sample(i))
        assertEquals(StatsRing.CAPACITY, ring.size)
        assertEquals(6, ring.value(StatsRing.CPU, 0))
        assertEquals(StatsRing.CAPACITY + 5, ring.latest(StatsRing.CPU))
        assertEquals(StatsRing.CAPACITY + 5, ring.value(StatsRing.CPU, StatsRing.CAPACITY - 1))
        ring.clear()
        assertEquals(0, ring.size)
        assertEquals(-1, ring.latest(StatsRing.GPU_POWER))
    }

    @Test
    fun aWindowThatResetBeforeNowHasNoPercent() {
        val limit = LimitEntry(AgentToken.CODEX, LimitWindow.Minutes(300), 42, 1000, 900)
        assertEquals(42, limit.pctAt(999))
        assertEquals(42, limit.pctAt(1000))
        assertEquals(-1, limit.pctAt(1001))
        assertEquals(42, limit.copy(resetAtMin = -1).pctAt(5000))
    }

    @Test
    fun theUnpricedFlagIsRead() {
        assertTrue(DashWire.spend("codex/1/2/3/4/5/6/7/1").single().unpriced)
        assertEquals(false, DashWire.spend("codex/1/2/3/4/5/6/7/0").single().unpriced)
    }

    @Test
    fun hostileAndShortPayloadsAreEmptyNotErrors() {
        assertNull(DashWire.stats("1/2/3"))
        assertNull(DashWire.statsInfo("a|b"))
        assertEquals(emptyList<DiskEntry>(), DashWire.disks("garbage"))
        assertEquals(emptyList<SessionEntry>(), DashWire.sessions("||::|"))
        assertEquals(emptyList<LimitEntry>(), DashWire.limits(":::::|x"))
        assertEquals(emptyList<SpendEntry>(), DashWire.spend("claude/1"))
        assertNull(DashWire.weather(""))
        assertNull(DashWire.permLabel("nope"))
        assertNull(DashWire.permAsk("0123456789ab", true, ByteArray(5000)))
        assertEquals(emptyList<GithubRow>(), DashWire.github(ByteArray(0)))
    }

    @Test
    fun limitWindowsParseToTheThreeForms() {
        assertEquals(LimitWindow.Minutes(300), DashWire.limitWindow("300"))
        assertEquals(LimitWindow.Minutes(10080), DashWire.limitWindow("10080"))
        assertEquals(LimitWindow.Minutes(43200), DashWire.limitWindow("43200"))
        assertEquals(LimitWindow.Context, DashWire.limitWindow("ctx"))
        assertEquals(LimitWindow.Named("spend_limit"), DashWire.limitWindow("~spend_limit"))
        val bad = listOf("~", "5h", "0", "600000", "-5", "0300", "3.5", "CTX", "")
        for (text in bad) assertNull(text, DashWire.limitWindow(text))
    }

    @Test
    fun aBidiMarkerSurvivesAsPlainText() {
        val body = "claude\nBash\nrm <U+202E>fdp.sh".toByteArray(Charsets.UTF_8)
        assertEquals("rm <U+202E>fdp.sh", DashWire.permAsk("0123456789ab", true, body)?.text)
    }

    @Test
    fun aRawControlOrBidiCharacterFromTheLaptopIsShownAsAMarker() {
        // Built from code points: the raw characters themselves are what the laptop might send.
        val lf = Char(0x0A)
        val raw = "claude" + lf + "Bash" + lf + "rm " + Char(0x202E) + "fdp.sh" + Char(0x85) + Char(0x0D)
        val body = raw.toByteArray(Charsets.UTF_8)
        assertEquals("rm <U+202E>fdp.sh<U+0085><U+000D>", DashWire.permAsk("0123456789ab", true, body)?.text)
    }

    @Test
    fun permAnswerIsByteExact() {
        assertEquals("a1b2c3d4e5f6/a", PermAnswer.text("a1b2c3d4e5f6", true))
        assertEquals("a1b2c3d4e5f6/d", PermAnswer.text("a1b2c3d4e5f6", false))
    }

    @Test
    fun parsesTheTypedFields() {
        val session = SessionEntry(AgentToken.CODEX, SessionStateToken.WAIT, "x", 42)
        assertEquals(session, DashWire.sessions("codex:wait:42:x").single())
        assertEquals(-1, DashWire.sessions("codex:wait:101:x").single().contextPct)
        assertEquals(emptyList<SessionEntry>(), DashWire.sessions("codex:wait:x"))
        assertEquals(7L, DashWire.spend("claude/1/2/3/4/5/6/7/0").single().centsMonth)
        assertEquals(emptyList<SpendEntry>(), DashWire.spend("claude/1/2/3/4/5/6/7/8/9/0"))
        assertEquals(CiToken.FAIL, DashWire.github("R\ta/b\t1\tt\t\tfail".toByteArray()).single().ci)
    }

    private fun sample(v: Int) = StatsSample(v, v, v, v, v, v, v, v, v, v, v)

    private fun unescape(text: String): String {
        val out = StringBuilder()
        var i = 0
        while (i < text.length) {
            val c = text[i]
            if (c != '\\') {
                out.append(c)
                i++
                continue
            }
            when (text[i + 1]) {
                't' -> {
                    out.append('\t')
                    i += 2
                }

                'n' -> {
                    out.append('\n')
                    i += 2
                }

                '\\' -> {
                    out.append('\\')
                    i += 2
                }

                'u' -> {
                    val end = text.indexOf('}', i)
                    out.appendCodePoint(text.substring(i + 3, end).toInt(16))
                    i = end + 1
                }

                '{' -> {
                    val end = text.indexOf('}', i)
                    val star = text.indexOf('*', i)
                    val unit = text.substring(star + 1, end)
                    repeat(text.substring(i + 2, star).toInt()) { out.append(unit) }
                    i = end + 1
                }

                else -> {
                    error("bad escape in $text")
                }
            }
        }
        return out.toString()
    }

    private fun vectorFile(): File {
        var dir: File? = File("").absoluteFile
        while (dir != null) {
            val candidate = File(dir, "protocol/dashboard-vectors.txt")
            if (candidate.isFile) return candidate
            dir = dir.parentFile
        }
        error("protocol/dashboard-vectors.txt was not found above ${File("").absolutePath}")
    }

    private companion object {
        const val ARROW = " => "
        const val NONE = "<none>"
        const val EMPTY = "<empty>"
    }
}
