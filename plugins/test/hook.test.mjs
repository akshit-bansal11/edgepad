// Tests for plugins/edgepad/scripts/edgepad-hook.mjs: run with `node --test plugins/test`.
// Each test runs the real script as the agents do and talks to a fake hook listener.
import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { chmodSync, existsSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { startServer, TOKEN, writeHookFile } from './fake-server.mjs'

const SCRIPT = fileURLToPath(new URL('../edgepad/scripts/edgepad-hook.mjs', import.meta.url))
const VECTORS = fileURLToPath(new URL('../../protocol/hook-vectors.txt', import.meta.url))

const ALLOW = '{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow"}}}\n'
const DENY =
    '{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"deny","message":"Denied from Edgepad on the phone."}}}\n'

// Runs the hook client; resolves { code, stdout, stderr, ms }.
const run = (args, { stdin = '', file, env = {} } = {}) =>
    new Promise(resolve => {
        const started = Date.now()
        const child = spawn(process.execPath, [SCRIPT, ...args], {
            env: { ...process.env, EDGEPAD_HOOK_FILE: file ?? '', ...env },
            stdio: ['pipe', 'pipe', 'pipe'],
        })
        let stdout = ''
        let stderr = ''
        child.stdout.on('data', c => (stdout += c))
        child.stderr.on('data', c => (stderr += c))
        child.stdin.on('error', () => {}) // the client may stop reading an oversize stdin
        child.stdin.end(stdin)
        child.on('close', code => resolve({ code, stdout, stderr, ms: Date.now() - started }))
    })

const permHook = {
    session_id: 's1',
    transcript_path: '/t.jsonl',
    cwd: '/home/u/project',
    permission_mode: 'default',
    hook_event_name: 'PermissionRequest',
    tool_name: 'Bash',
    tool_input: { command: 'ls -la', description: 'List files', timeout: 5 },
    agent: 'codex', // never trusted: the agent comes from argv
}

const withServer = async (opts, body) => {
    const server = await startServer(opts)
    try {
        await body(server)
    } finally {
        await server.close()
    }
}

// The request the server received, minus the token, which is checked separately.
const request = server => {
    assert.equal(server.lines.length, 1, 'exactly one request line')
    const req = JSON.parse(server.lines[0])
    assert.equal(req.token, TOKEN)
    delete req.token
    return req
}

test('perm allow prints the allow decision', () =>
    withServer({ reply: '{"v":1,"decision":"allow"}' }, async server => {
        const r = await run(['claude', 'perm'], { stdin: JSON.stringify(permHook), file: server.file })
        assert.equal(r.code, 0)
        assert.equal(r.stdout, ALLOW)
        assert.deepEqual(request(server), {
            v: 1,
            agent: 'claude',
            kind: 'perm',
            d: { session_id: 's1', cwd: '/home/u/project', tool_name: 'Bash', tool_input: { command: 'ls -la' } },
        })
    }))

test('perm deny prints the deny decision with the fixed message', () =>
    withServer({ reply: '{"v":1,"decision":"deny","message":"from the server, never shown"}' }, async server => {
        const r = await run(['codex', 'perm'], { stdin: JSON.stringify(permHook), file: server.file })
        assert.equal(r.code, 0)
        assert.equal(r.stdout, DENY)
        assert.equal(request(server).agent, 'codex')
    }))

test('perm defer, a bad reply, or a wrong version print nothing', async () => {
    for (const reply of [
        '{"v":1,"decision":"defer"}',
        '{"v":2,"decision":"allow"}',
        '{"v":1,"decision":"ALLOW"}',
        'not json',
        '{"v":1,"ok":true}',
    ]) {
        await withServer({ reply }, async server => {
            const r = await run(['claude', 'perm'], { stdin: JSON.stringify(permHook), file: server.file })
            assert.equal(r.code, 0, reply)
            assert.equal(r.stdout, '', reply)
        })
    }
})

test('perm sends only whitelisted tool_input fields, cut to 5000 characters', () =>
    withServer({ reply: '{"v":1,"decision":"defer"}' }, async server => {
        const hook = {
            ...permHook,
            tool_name: 'Edit',
            tool_input: {
                file_path: 'x'.repeat(6000),
                url: 'https://example.com',
                command: ['not', 'a', 'string'],
                old_string: 'secret',
                new_string: 'secret',
            },
        }
        await run(['claude', 'perm'], { stdin: JSON.stringify(hook), file: server.file })
        const d = request(server).d
        assert.deepEqual(Object.keys(d.tool_input).sort(), ['file_path', 'url'])
        assert.equal(d.tool_input.file_path.length, 5000)
    }))

test('the 5000-character cut never leaves half a surrogate pair', () =>
    withServer({ reply: '{"v":1,"decision":"defer"}' }, async server => {
        const command = `${'a'.repeat(4999)}😀tail`
        await run(['claude', 'perm'], { stdin: JSON.stringify({ ...permHook, tool_input: { command } }), file: server.file })
        assert.equal(request(server).d.tool_input.command, 'a'.repeat(4999))
    }))

test('a server that never replies: perm prints nothing and exits', () =>
    withServer({ reply: null }, async server => {
        const r = await run(['claude', 'perm'], {
            stdin: JSON.stringify(permHook),
            file: server.file,
            env: { EDGEPAD_HOOK_WAIT_MS: '1000' },
        })
        assert.equal(r.code, 0)
        assert.equal(r.stdout, '')
        assert.ok(r.ms < 10_000, `took ${r.ms} ms`)
    }))

test('session and usage exit before any reply and print nothing', async () => {
    const cases = [
        [['claude', 'session'], JSON.stringify({ ...permHook, hook_event_name: 'Stop' }), undefined],
        [['claude', 'usage', '{"rateLimits":[{"kind":"five_hour","percentUsed":12}]}'], '', undefined],
        [['claude', 'spend', '{"today":{"tok":1,"cents":2}}'], '', undefined],
    ]
    for (const [args, stdin] of cases) {
        await withServer({ reply: '{"v":1,"ok":true}', delayMs: 5000 }, async server => {
            const r = await run(args, { stdin, file: server.file })
            assert.equal(r.code, 0)
            assert.equal(r.stdout, '')
            assert.equal(server.state.replied, false, `${args[1]} waited for the reply`)
            await server.waitLine()
            assert.equal(request(server).kind, args[1])
        })
    }
})

test('usage and spend send their JSON argument as d', () =>
    withServer({ reply: '{"v":1,"ok":true}' }, async server => {
        const d = { rateLimits: [{ kind: 'seven_day', percentUsed: 40.5, resetsAt: '2026-11-20T00:00:00Z' }], context: 7 }
        await run(['codex', 'usage', JSON.stringify(d)], { file: server.file })
        await server.waitLine()
        assert.deepEqual(request(server), { v: 1, agent: 'codex', kind: 'usage', d })
    }))

test('usage over 8 KB, not JSON, or not an object: nothing is sent', async () => {
    for (const arg of [JSON.stringify({ x: 'y'.repeat(9000) }), 'garbage', '[1,2]', '42', 'null', undefined]) {
        await withServer({ reply: '{"v":1,"ok":true}' }, async server => {
            const r = await run(['claude', 'usage', ...(arg === undefined ? [] : [arg])], { file: server.file })
            assert.equal(r.code, 0)
            assert.equal(r.stdout, '')
            await new Promise(resolve => setTimeout(resolve, 200))
            assert.equal(server.lines.length, 0, String(arg).slice(0, 20))
        })
    }
})

test('Notification types are filtered and the prompt never leaves', async () => {
    const send = async hook =>
        withServer({ reply: '{"v":1,"ok":true}' }, async server => {
            await run(['claude', 'session'], { stdin: JSON.stringify(hook), file: server.file })
            await server.waitLine(500)
            return server.lines.map(line => JSON.parse(line).d)
        })
    const base = { session_id: 's2', cwd: '/w', transcript_path: '/t' }
    for (const type of ['permission_prompt', 'idle_prompt', 'agent_needs_input']) {
        const sent = await send({ ...base, hook_event_name: 'Notification', notification_type: type, message: 'm' })
        assert.deepEqual(sent, [{ hook_event_name: 'Notification', session_id: 's2', cwd: '/w', notification_type: type }])
    }
    for (const type of ['auth_success', 'agent_completed', undefined]) {
        assert.deepEqual(await send({ ...base, hook_event_name: 'Notification', notification_type: type }), [], type)
    }
    assert.deepEqual(await send({ ...base, hook_event_name: 'UserPromptSubmit', prompt: 'private words' }), [
        { hook_event_name: 'UserPromptSubmit', session_id: 's2', cwd: '/w' },
    ])
    assert.deepEqual(await send({ ...base, hook_event_name: 'PreToolUse' }), [])
})

test('no hook.json, a dead endpoint, or a bad hook.json: exit 0 with empty stdout', async () => {
    const server = await startServer({ reply: '{"v":1,"decision":"allow"}' })
    try {
        const stdin = JSON.stringify(permHook)
        const missing = await run(['claude', 'perm'], { stdin, file: join(server.dir, 'absent.json') })
        assert.deepEqual([missing.code, missing.stdout], [0, ''])

        const dead = join(server.dir, 'dead.json')
        const deadEndpoint =
            process.platform === 'win32' ? '\\\\.\\pipe\\edgepad-hook-0000000000000000' : join(server.dir, 'gone.sock')
        writeHookFile(dead, deadEndpoint)
        const r = await run(['claude', 'perm'], { stdin, file: dead })
        assert.deepEqual([r.code, r.stdout], [0, ''])

        for (const body of ['nope', '{"v":2}', `{"v":1,"endpoint":"${'x'}","token":"short"}`]) {
            const bad = join(server.dir, 'bad.json')
            writeFileSync(bad, body, { mode: 0o600 })
            const b = await run(['claude', 'perm'], { stdin, file: bad })
            assert.deepEqual([b.code, b.stdout], [0, ''], body)
        }
        assert.equal(server.lines.length, 0)
    } finally {
        await server.close()
    }
})

test('a hook.json readable by group or others is refused (Unix)', { skip: process.platform === 'win32' }, () =>
    withServer({ reply: '{"v":1,"decision":"allow"}' }, async server => {
        for (const mode of [0o644, 0o640, 0o604, 0o620]) {
            chmodSync(server.file, mode)
            const r = await run(['claude', 'perm'], { stdin: JSON.stringify(permHook), file: server.file })
            assert.deepEqual([r.code, r.stdout], [0, ''], mode.toString(8))
        }
        assert.equal(server.lines.length, 0)
        assert.equal(server.state.connections, 0)
    }))

test('oversize or garbage stdin, a bad agent or kind: exit 0, nothing sent', () =>
    withServer({ reply: '{"v":1,"decision":"allow"}' }, async server => {
        const big = JSON.stringify({ ...permHook, tool_input: { command: 'x'.repeat(1 << 20) } })
        const cases = [
            [['claude', 'perm'], big],
            [['claude', 'perm'], 'garbage{'],
            [['claude', 'perm'], '[]'],
            [['claude', 'perm'], JSON.stringify({ ...permHook, tool_name: '' })],
            [['gemini', 'perm'], JSON.stringify(permHook)],
            [['claude', 'shell'], JSON.stringify(permHook)],
            [[], ''],
        ]
        for (const [args, stdin] of cases) {
            const r = await run(args, { stdin, file: server.file })
            assert.deepEqual([r.code, r.stdout], [0, ''], args.join(' '))
        }
        await new Promise(resolve => setTimeout(resolve, 200))
        assert.equal(server.lines.length, 0)
    }))

test('the token is never printed', () =>
    withServer({ reply: '{"v":1,"decision":"allow"}' }, async server => {
        const r = await run(['claude', 'perm'], { stdin: JSON.stringify(permHook), file: server.file })
        assert.ok(!r.stdout.includes(TOKEN) && !r.stderr.includes(TOKEN))
    }))

// Vectors: inline, plus the shared file's `CLIENT` lines once S8 writes protocol/hook-vectors.txt.
// Line format: CLIENT <agent> <kind> <stdin JSON or usage/spend argument> => <request JSON without token | NONE>
const INLINE = [
    'CLIENT claude session {"hook_event_name":"SessionStart","session_id":"a","cwd":"C:\\\\w","source":"startup"} => {"v":1,"agent":"claude","kind":"session","d":{"hook_event_name":"SessionStart","session_id":"a","cwd":"C:\\\\w"}}',
    'CLIENT codex session {"hook_event_name":"SessionEnd","session_id":"b","cwd":"/w","reason":"other"} => {"v":1,"agent":"codex","kind":"session","d":{"hook_event_name":"SessionEnd","session_id":"b","cwd":"/w"}}',
    'CLIENT claude session {"hook_event_name":"Stop","cwd":7} => {"v":1,"agent":"claude","kind":"session","d":{"hook_event_name":"Stop","session_id":"","cwd":""}}',
    'CLIENT claude perm {"tool_name":"WebFetch","tool_input":{"url":"https://x.test/a\\nb"}} => {"v":1,"agent":"claude","kind":"perm","d":{"session_id":"","cwd":"","tool_name":"WebFetch","tool_input":{"url":"https://x.test/a\\nb"}}}',
    'CLIENT claude perm {"tool_name":"Bash"} => {"v":1,"agent":"claude","kind":"perm","d":{"session_id":"","cwd":"","tool_name":"Bash","tool_input":{}}}',
    'CLIENT claude session {"hook_event_name":"Notification","notification_type":"elicitation_dialog"} => NONE',
]

// TODO(S8): protocol/hook-vectors.txt does not exist yet; its CLIENT lines run here once it does.
const fileVectors = () =>
    existsSync(VECTORS)
        ? readFileSync(VECTORS, 'utf8')
              .split('\n')
              .map(line => line.replace(/\r$/, ''))
              .filter(line => line.startsWith('CLIENT '))
        : []

test('request vectors', async () => {
    for (const vector of [...INLINE, ...fileVectors()]) {
        const at = vector.indexOf(' => ')
        const [, agent, kind, ...rest] = vector.slice(0, at).split(' ')
        const input = rest.join(' ')
        const want = vector.slice(at + 4)
        await withServer({ reply: kind === 'perm' ? '{"v":1,"decision":"defer"}' : '{"v":1,"ok":true}' }, async server => {
            const argv = kind === 'perm' || kind === 'session' ? [agent, kind] : [agent, kind, input]
            const stdin = kind === 'perm' || kind === 'session' ? input : ''
            const r = await run(argv, { stdin, file: server.file })
            assert.deepEqual([r.code, r.stdout], [0, ''], vector)
            await server.waitLine(want === 'NONE' ? 300 : 3000)
            if (want === 'NONE') assert.equal(server.lines.length, 0, vector)
            else assert.deepEqual(request(server), JSON.parse(want), vector)
        })
    }
})
