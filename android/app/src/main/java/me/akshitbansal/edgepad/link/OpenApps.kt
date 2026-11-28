package me.akshitbansal.edgepad.link

private const val OPEN_APPS_MAX = 40

/**
 * TEXT OPEN_APPS: lines of `token`, a tab, `name`. A line with no tab or a bad token is dropped, a token seen twice
 * keeps its first line, and at most 40 are kept. The name goes through [ForegroundApp.parse], so it is cleaned as the
 * app in front is. Names are personal data: never log them.
 */
fun parseOpenApps(text: String): List<ForegroundApp> {
    val seen = HashSet<String>()
    val out = ArrayList<ForegroundApp>()
    for (line in text.split('\n')) {
        val tab = line.indexOf('\t')
        if (tab < 0 || out.size >= OPEN_APPS_MAX) continue
        val app = ForegroundApp.parse(line.substring(0, tab) + "/" + line.substring(tab + 1)) ?: continue
        if (seen.add(app.token)) out.add(app)
    }
    return out
}
