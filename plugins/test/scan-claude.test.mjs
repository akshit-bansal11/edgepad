// Tests for the Claude spend scanner (plugins/edgepad/scripts/scan-claude.mjs, prices.mjs, cache.mjs).
import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { appendFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, utimesSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { cachePaths } from '../edgepad/scripts/cache.mjs'
import { costOf, isPriced } from '../edgepad/scripts/prices.mjs'
import { periods, scan, takeLock } from '../edgepad/scripts/scan-claude.mjs'
import { startServer, TOKEN } from './fake-server.mjs'

const SCRIPT = fileURLToPath(new URL('../edgepad/scripts/scan-claude.mjs', import.meta.url))

// One assistant response line, as Claude Code writes it.
const line = (id, at, model, usage, extra = {}) =>
    `${JSON.stringify({ type: 'assistant', timestamp: at.toISOString(), message: { id, model, usage }, ...extra })}\n`

// A config dir whose projects folder holds the given files ({ 'p1/a.jsonl': text }), mtimes set to `mtime`.
const configWith = (files, mtime = new Date()) => {
    const dir = mkdtempSync(join(tmpdir(), 'edgepad-scan-'))
    for (const [name, text] of Object.entries(files)) {
        const path = join(dir, 'projects', ...name.split('/'))
        mkdirSync(join(path, '..'), { recursive: true })
        writeFileSync(path, text)
        utimesSync(path, mtime, mtime)
    }
    return dir
}

// Wednesday 18 November 2026, noon local time: the week starts Monday the 16th.
const NOW = new Date(2026, 10, 18, 12)
const local = (d, h, m = 0) => new Date(2026, 10, d, h, m)

const transcript =
    // the same id twice: only the last (final) usage counts
    line('A', local(18, 10), 'claude-opus-5-5', { input_tokens: 1, output_tokens: 1 }) +
    line('A', local(18, 10), 'claude-opus-5-5', {
        input_tokens: 1e6,
        output_tokens: 1e5,
        cache_read_input_tokens: 1e6,
    }) + // 4 + 2 + 0.2 = $6.20, 1,100,000 tokens (cache reads are not tokens)
    line('B', local(16, 8), 'claude-sonnet-4-5', { input_tokens: 2e5, output_tokens: 1e5 }) + // $2.10, Monday
    line('C', local(15, 23, 30), 'claude-haiku-4-5', { input_tokens: 1e6 }) + // $1.00, Sunday: month only
    line('D', new Date(2026, 9, 31, 12), 'claude-opus-5-5', { input_tokens: 1e6 }) + // October: nothing
    line('E', local(18, 9), '<synthetic>', { input_tokens: 1e6 }) +
    `${JSON.stringify({ type: 'user', timestamp: local(18, 9).toISOString(), message: { id: 'F', usage: { input_tokens: 9 } } })}\n` +
    'not json "usage"\n' +
    'null\n' +
    line('H', local(18, 9), 'claude-opus-5-5', { input_tokens: 'many', output_tokens: -5 }) + // hostile: nothing
    `${JSON.stringify({ type: 'assistant', timestamp: 'x', message: { id: 'I', model: 'claude-opus-5-5', usage: { input_tokens: 1 } } })}\n`

test('today, week and month totals with hand-computed prices', async () => {
    const dir = configWith({ 'p1/a.jsonl': transcript }, NOW)
    try {
        assert.deepEqual(await scan({ configDir: dir, now: NOW }), {
            today: { tok: 1_100_000, cents: 620 },
            week: { tok: 1_400_000, cents: 830 },
            month: { tok: 2_400_000, cents: 930 },
            unpriced: false,
        })
    } finally {
        rmSync(dir, { recursive: true, force: true })
    }
})

test('a forked or copied transcript counts its shared responses once', async () => {
    const dir = configWith({ 'p1/a.jsonl': transcript, 'p2/fork.jsonl': transcript }, NOW)
    try {
        const r = await scan({ configDir: dir, now: NOW })
        assert.deepEqual(r.month, { tok: 2_400_000, cents: 930 })
    } finally {
        rmSync(dir, { recursive: true, force: true })
    }
})

test('a model missing from the table counts its tokens, adds no dollars and is flagged', async () => {
    const dir = configWith({ 'p/a.jsonl': line('G', local(18, 11), 'gpt-9-turbo', { input_tokens: 10, output_tokens: 5 }) }, NOW)
    try {
        assert.deepEqual(await scan({ configDir: dir, now: NOW }), {
            today: { tok: 15, cents: 0 },
            week: { tok: 15, cents: 0 },
            month: { tok: 15, cents: 0 },
            unpriced: true,
        })
    } finally {
        rmSync(dir, { recursive: true, force: true })
    }
})

test('the cache is used while a file is unchanged and dropped when it changes', async () => {
    const dir = configWith({ 'p/a.jsonl': transcript }, NOW)
    const path = join(dir, 'projects', 'p', 'a.jsonl')
    const cacheFile = cachePaths(dir, join).file
    try {
        await scan({ configDir: dir, now: NOW })
        const cache = JSON.parse(readFileSync(cacheFile, 'utf8'))
        assert.ok(cache[path], 'the file is cached')
        // A doctored cache entry is believed while size and mtime match: proof the file was not re-read.
        cache[path].rows = { Z: ['2026-11-18', 7, 0.07, 1] }
        writeFileSync(cacheFile, JSON.stringify(cache))
        assert.deepEqual((await scan({ configDir: dir, now: NOW })).today, { tok: 7, cents: 7 })
        // Appending changes size and mtime: the file is summed again.
        appendFileSync(path, line('J', local(18, 11), 'claude-opus-5-5', { output_tokens: 1000 }))
        utimesSync(path, NOW, new Date(NOW.getTime() + 1000))
        assert.deepEqual((await scan({ configDir: dir, now: NOW })).today, { tok: 1_101_000, cents: 622 })
    } finally {
        rmSync(dir, { recursive: true, force: true })
    }
})

test('the cache lives in <config>/edgepad, apart from Plimsoll', () => {
    assert.deepEqual(cachePaths('/c', (...p) => p.join('/')), {
        dir: '/c/edgepad',
        file: '/c/edgepad/scan-cache-1.json',
        lock: '/c/edgepad/scan.lock',
    })
})

test('week boundaries in local time start on Monday', () => {
    // Monday 00:30: the week is just today; Sunday night belongs to last week.
    assert.deepEqual(periods(new Date(2026, 10, 16, 0, 30)), {
        today: '2026-11-16',
        weekStart: '2026-11-16',
        monthStart: '2026-11-01',
        from: '2026-11-01',
    })
    // Sunday 23:59 is still the week that began on Monday the 9th.
    assert.equal(periods(new Date(2026, 10, 15, 23, 59)).weekStart, '2026-11-09')
    // Tuesday 1 December: the week began in November, so the scan reaches back to it.
    assert.deepEqual(periods(new Date(2026, 11, 1, 9)), {
        today: '2026-12-01',
        weekStart: '2026-11-30',
        monthStart: '2026-12-01',
        from: '2026-11-30',
    })
})

test('a week that starts last month is counted in the week, not the month', async () => {
    const now = new Date(2026, 11, 1, 9)
    const text =
        line('M', new Date(2026, 10, 30, 22), 'claude-sonnet-4-5', { input_tokens: 1e6 }) + // Monday 30 Nov, $3
        line('N', new Date(2026, 10, 29, 22), 'claude-sonnet-4-5', { input_tokens: 1e6 }) // Sunday: neither
    const dir = configWith({ 'p/a.jsonl': text }, now)
    try {
        const r = await scan({ configDir: dir, now })
        assert.deepEqual(r.today, { tok: 0, cents: 0 })
        assert.deepEqual(r.week, { tok: 1e6, cents: 300 })
        assert.deepEqual(r.month, { tok: 0, cents: 0 })
    } finally {
        rmSync(dir, { recursive: true, force: true })
    }
})

test('no projects folder: zero totals', async () => {
    const dir = mkdtempSync(join(tmpdir(), 'edgepad-scan-'))
    try {
        const zero = { tok: 0, cents: 0 }
        assert.deepEqual(await scan({ configDir: dir, now: NOW }), { today: zero, week: zero, month: zero, unpriced: false })
    } finally {
        rmSync(dir, { recursive: true, force: true })
    }
})

test('price math against hand-computed values', () => {
    const u = {
        input_tokens: 1e6,
        output_tokens: 1e6,
        cache_read_input_tokens: 1e6,
        cache_creation_input_tokens: 2e6,
        cache_creation: { ephemeral_1h_input_tokens: 1e6 },
    }
    // opus-5-5: 4 + 20 + 4*0.05 + 4*1.25 + 4*2 = 37.2
    const want = {
        'claude-opus-5-5': 37.2,
        'claude-opus-5': 46.75,
        'claude-opus-4-5-20251101': 46.75,
        'claude-sonnet-4-5@20250929': 28.05,
        'us.anthropic.claude-opus-4-1-20250805-v1:0': 140.25,
        'claude-haiku-5-5': 4.675,
        'no-such-model': 0,
    }
    for (const [model, usd] of Object.entries(want)) {
        assert.ok(Math.abs(costOf(model, u) - usd) < 1e-9, `${model}: ${costOf(model, u)} != ${usd}`)
    }
    assert.ok(Math.abs(costOf('claude-haiku-5-5', { input_tokens: 1000, output_tokens: 1000 }) - 0.0006) < 1e-12)
    assert.equal(costOf('claude-opus-5-5', {}), 0)
    assert.equal(isPriced('claude-sonnet-4-5@20250929'), true)
    assert.equal(isPriced('gpt-9-turbo'), false)
})

test('the scan lock allows one scan per 30 s', () => {
    const dir = mkdtempSync(join(tmpdir(), 'edgepad-lock-'))
    const lock = join(dir, 'edgepad', 'scan.lock')
    try {
        const t = Date.now()
        assert.equal(takeLock(lock, t), true)
        assert.equal(takeLock(lock, t + 1000), false)
        assert.equal(takeLock(lock, t + 29_000), false)
        assert.equal(takeLock(lock, t + 31_000), true)
    } finally {
        rmSync(dir, { recursive: true, force: true })
    }
})

const runScript = (args, env) =>
    new Promise(resolve => {
        const started = Date.now()
        const child = spawn(process.execPath, [SCRIPT, ...args], { env: { ...process.env, ...env }, stdio: ['pipe', 'pipe', 'pipe'] })
        let stdout = ''
        child.stdout.on('data', c => (stdout += c))
        child.stdin.on('error', () => {}) // the launcher does not read its stdin
        child.stdin.end('{"hook_event_name":"Stop"}')
        child.on('close', code => resolve({ code, stdout, ms: Date.now() - started }))
    })

test('the hook entry returns at once and a detached scan sends one spend message', async () => {
    const now = new Date()
    const dir = configWith({ 'p/a.jsonl': line('R', now, 'claude-sonnet-4-5', { input_tokens: 1e6, output_tokens: 0 }) }, now)
    const server = await startServer({ reply: '{"v":1,"ok":true}' })
    try {
        const env = { CLAUDE_CONFIG_DIR: dir, EDGEPAD_HOOK_FILE: server.file }
        const r = await runScript([], env)
        assert.deepEqual([r.code, r.stdout], [0, ''])
        await server.waitLine(10_000)
        assert.equal(server.lines.length, 1)
        const req = JSON.parse(server.lines[0])
        assert.equal(req.token, TOKEN)
        assert.equal(req.agent, 'claude')
        assert.equal(req.kind, 'spend')
        assert.deepEqual(req.d.today, { tok: 1e6, cents: 300 })
        // A second hook within 30 s starts no second scan.
        const again = await runScript([], env)
        assert.deepEqual([again.code, again.stdout], [0, ''])
        await new Promise(resolve => setTimeout(resolve, 1500))
        assert.equal(server.lines.length, 1)
    } finally {
        await server.close()
        rmSync(dir, { recursive: true, force: true, maxRetries: 5 })
    }
})
