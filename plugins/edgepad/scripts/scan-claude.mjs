// Adapted from Plimsoll by Akshit Bansal, MIT (scripts/scan.mjs).
//
// Sums the tokens in Claude Code's transcripts for today, this week (from Monday) and this month,
// in local time, with dollars estimated at API list prices (prices.mjs; a subscription is not
// billed this way), and hands them to the Edgepad tray app as a `spend` message through
// edgepad-hook.mjs. Tokens are input + output; cache reads and writes are not counted as tokens
// (they are priced). Plimsoll's activity stats are not computed here.
//
//   node scan-claude.mjs          the hook entry (Stop, SessionStart): starts the scan as a detached
//                                 child and returns at once; at most one scan per 30 s (lock file)
//   node scan-claude.mjs --run    the scan itself
//
// Prints nothing and always exits 0.
import { execFile, spawn } from 'node:child_process'
import {
    createReadStream,
    mkdirSync,
    readFileSync,
    readdirSync,
    realpathSync,
    renameSync,
    statSync,
    unlinkSync,
    writeFileSync,
} from 'node:fs'
import { homedir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { createInterface } from 'node:readline'
import { fileURLToPath } from 'node:url'
import { cachePaths } from './cache.mjs'
import { costOf, isPriced } from './prices.mjs'

const SELF = fileURLToPath(import.meta.url)
const LOCK_MS = 30_000

const dayOf = date =>
    `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`

// The first day of each period, local time; `from` is the earliest of them (a week can start last month).
export const periods = now => {
    const today = dayOf(now)
    const weekStart = dayOf(new Date(now.getFullYear(), now.getMonth(), now.getDate() - ((now.getDay() + 6) % 7)))
    const monthStart = dayOf(new Date(now.getFullYear(), now.getMonth(), 1))
    return { today, weekStart, monthStart, from: weekStart < monthStart ? weekStart : monthStart }
}

// One file's responses on or after `from`, by message id: [day, tokens, usd, priced 1|0].
// A response is written once per content block under one message id, its usage final on the
// last, so the last row of each id is the one kept.
const sumFile = async (path, from) => {
    const rows = {}
    const lines = createInterface({ input: createReadStream(path), crlfDelay: Infinity })
    for await (const line of lines) {
        if (!line.includes('"usage"')) continue
        let row
        try {
            row = JSON.parse(line)
        } catch {
            continue
        }
        const usage = row?.message?.usage
        if (!usage || typeof usage !== 'object' || row.type !== 'assistant' || row.message.model === '<synthetic>') {
            continue
        }
        const at = Date.parse(row.timestamp)
        if (Number.isNaN(at)) continue // would bucket under NaN-NaN-NaN
        const day = dayOf(new Date(at))
        if (day < from) continue
        const id = row.message.id ?? row.uuid
        if (typeof id !== 'string') continue
        const tokens = num(usage.input_tokens) + num(usage.output_tokens)
        // num(): a hostile or odd usage field (a string, a negative) counts as nothing, never NaN
        rows[id] = [day, tokens, num(costOf(row.message.model, usage)), isPriced(row.message.model) ? 1 : 0]
    }
    return rows
}
const num = x => (Number.isFinite(x) && x > 0 ? x : 0)

// { today, week, month: { tok, cents }, unpriced } for the transcripts under configDir/projects.
// Responses are counted once by message id across all files, so a forked or copied transcript
// does not count its shared responses twice.
export const scan = async ({ configDir, now = new Date() }) => {
    const { today, weekStart, monthStart, from } = periods(now)
    const fromMs = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 40).getTime()
    const root = join(configDir, 'projects')
    const cachePath = cachePaths(configDir, join).file
    let cache = {}
    try {
        cache = JSON.parse(readFileSync(cachePath, 'utf8'))
    } catch {
        // no cache yet, or an unreadable one: every file is summed again
    }
    let names = []
    try {
        names = readdirSync(root, { recursive: true })
    } catch {
        // no projects folder: nothing has been used, the totals stay zero
    }
    const kept = {}
    const byId = new Map()
    for (const name of names) {
        if (!name.endsWith('.jsonl')) continue
        const path = join(root, name)
        let mtimeMs, size, rows
        try {
            ;({ mtimeMs, size } = statSync(path))
            // ponytail: a file last written over 40 days ago holds nothing for this week or month
            if (mtimeMs < fromMs) continue
            const hit = cache[path]
            rows =
                hit && hit.mtimeMs === mtimeMs && hit.size === size && hit.from <= from && hit.rows
                    ? hit.rows
                    : await sumFile(path, from)
        } catch {
            continue // a file removed mid-scan is skipped
        }
        kept[path] = { mtimeMs, size, from, rows }
        for (const [id, row] of Object.entries(rows)) byId.set(id, row)
    }
    saveCache(cachePath, kept) // drops files that no longer exist or fell out of the window

    const totals = { today: [0, 0], week: [0, 0], month: [0, 0] }
    let unpriced = false
    for (const [day, tokens, usd, priced] of byId.values()) {
        if (day < from) continue
        const add = sum => {
            sum[0] += tokens
            sum[1] += usd
        }
        if (day === today) add(totals.today)
        if (day >= weekStart) add(totals.week)
        if (day >= monthStart) add(totals.month)
        if (!priced && tokens > 0) unpriced = true
    }
    const out = ([tok, usd]) => ({ tok: Math.round(tok), cents: Math.round(usd * 100) })
    return { today: out(totals.today), week: out(totals.week), month: out(totals.month), unpriced }
}

// write then rename: a concurrent scan never reads a half-written cache
const saveCache = (cachePath, entries) => {
    const tmp = `${cachePath}.${process.pid}`
    try {
        mkdirSync(dirname(cachePath), { recursive: true })
        writeFileSync(tmp, JSON.stringify(entries))
        renameSync(tmp, cachePath)
    } catch {
        // the cache only saves time: the totals are right without it
        try {
            unlinkSync(tmp)
        } catch {}
    }
}

// True when no scan started in the last 30 s; then stamps the lock. ponytail: two hooks firing in
// the same instant may both start a scan; harmless, both send the same totals.
export const takeLock = (lock, nowMs = Date.now()) => {
    try {
        if (nowMs - statSync(lock).mtimeMs < LOCK_MS) return false
    } catch {
        // no lock yet
    }
    try {
        mkdirSync(dirname(lock), { recursive: true })
        writeFileSync(lock, String(process.pid))
        return true
    } catch {
        return false
    }
}

const configDirOf = () => process.env.CLAUDE_CONFIG_DIR || join(homedir(), '.claude')

const launch = () => {
    if (!takeLock(cachePaths(configDirOf(), join).lock)) return
    spawn(process.execPath, [SELF, '--run'], { detached: true, stdio: 'ignore', windowsHide: true }).unref()
}

const run = async () => {
    const totals = await scan({ configDir: configDirOf() })
    const hook = join(dirname(SELF), 'edgepad-hook.mjs')
    await new Promise(done =>
        execFile(process.execPath, [hook, 'claude', 'spend', JSON.stringify(totals)], { windowsHide: true, timeout: 10_000 }, () =>
            done(),
        ),
    )
}

const isMain = () => {
    try {
        const a = realpathSync(resolve(process.argv[1]))
        const b = realpathSync(SELF)
        return process.platform === 'win32' ? a.toLowerCase() === b.toLowerCase() : a === b
    } catch {
        return false
    }
}

if (process.argv[1] && isMain()) {
    const quit = () => process.exit(0)
    process.on('uncaughtException', quit)
    process.on('unhandledRejection', quit)
    if (process.argv[2] === '--run') run().then(quit, quit)
    else {
        launch()
        quit()
    }
}
