// Edgepad's one hook client, for Claude Code and Codex:
//
//   node edgepad-hook.mjs <claude|codex> <perm|session> < hook JSON on stdin
//   node edgepad-hook.mjs <claude|codex> <usage|spend> '<JSON>'
//
// Hands the event to the running Edgepad tray app over its local hook channel (a named pipe on
// Windows, a Unix socket elsewhere, both found through hook.json) and, for a permission request
// only, prints the app's decision in the agent's PermissionRequest output shape.
//
// Rules this file keeps, whatever happens:
// - Exit code is always 0. Exit 2 would block or alter a tool.
// - Nothing is printed except an allow or deny decision for `perm`. For SessionStart and
//   UserPromptSubmit, stdout reaches the model's context; for `perm`, no output means "defer":
//   the agent shows its normal prompt. So every error, timeout or refusal prints nothing.
// - The token and the request are never printed or logged.
// - The agent comes from argv, never from the payload. Only whitelisted fields leave this process.
// Uses only node: built-ins.
import { closeSync, fstatSync, openSync, readSync } from 'node:fs'
import { connect } from 'node:net'
import { homedir } from 'node:os'
import { join } from 'node:path'

const AGENTS = new Set(['claude', 'codex'])
const KINDS = new Set(['perm', 'session', 'usage', 'spend'])
// The lifecycle events the laptop's SessionBoard maps; anything else is not sent.
const EVENTS = new Set(['SessionStart', 'UserPromptSubmit', 'Notification', 'Stop', 'SessionEnd'])
// Notification types that mean "the agent waits for you"; other Notifications are not sent.
const NOTIFICATIONS = new Set(['permission_prompt', 'idle_prompt', 'agent_needs_input'])
// The only tool_input fields sent: Bash `command`; Edit/Write/MultiEdit/Read/NotebookEdit
// `file_path`; WebFetch `url`. Strings only. The laptop picks per tool and applies its 4096-byte rule.
const INPUT_FIELDS = ['command', 'file_path', 'url']
const INPUT_CAP = 5000 // characters, above the laptop's 4096-byte cap so its rule decides exactly
const STDIN_CAP = 1 << 20
const ARG_CAP = 8192
const HOOK_FILE_CAP = 4096
const REPLY_CAP = 4096
const CONNECT_MS = 2000
// The perm hook's own timeout is 120 s; the laptop answers within 30 s.
const replyWaitMs = () => {
    const n = Number(process.env.EDGEPAD_HOOK_WAIT_MS)
    return Number.isInteger(n) && n > 0 && n <= 35_000 ? n : 35_000
}
const DENY_MESSAGE = 'Denied from Edgepad on the phone.'

const quit = () => process.exit(0)
// Whatever goes wrong, print nothing and exit 0.
process.on('uncaughtException', quit)
process.on('unhandledRejection', quit)

// A string cut to `n` UTF-16 units without leaving half a surrogate pair; undefined for a non-string.
const cut = (value, n) => {
    if (typeof value !== 'string') return undefined
    if (value.length <= n) return value
    const code = value.charCodeAt(n - 1)
    return value.slice(0, code >= 0xd800 && code <= 0xdbff ? n - 1 : n)
}

// A tool_input field: a string cut to INPUT_CAP; for Codex's `command` also its array form, a list of strings (sent as is
// unless joined it passes INPUT_CAP, then as the cut joined string, which the laptop then offers Deny-only).
const MAX_PARTS = 1024
const field = (agent, key, value) => {
    if (typeof value === 'string' || agent !== 'codex' || key !== 'command') return cut(value, INPUT_CAP)
    if (!Array.isArray(value) || value.length === 0 || value.length > MAX_PARTS) return undefined
    if (!value.every(part => typeof part === 'string')) return undefined
    const joined = value.join(' ')
    return joined.length > INPUT_CAP ? cut(joined, INPUT_CAP) : value
}

// The request's `d` for a hook payload, or null when this event is not sent.
const buildData = (agent, kind, hook) => {
    if (hook === null || typeof hook !== 'object' || Array.isArray(hook)) return null
    if (kind === 'perm') {
        const tool = cut(hook.tool_name, 64)
        if (!tool) return null
        const input = {}
        const raw = hook.tool_input
        if (raw !== null && typeof raw === 'object' && !Array.isArray(raw)) {
            for (const key of INPUT_FIELDS) {
                const value = field(agent, key, raw[key])
                if (value !== undefined) input[key] = value
            }
        }
        return {
            session_id: cut(hook.session_id, 128) ?? '',
            cwd: cut(hook.cwd, 1024) ?? '',
            tool_name: tool,
            tool_input: input,
        }
    }
    if (kind === 'session') {
        const event = hook.hook_event_name
        if (!EVENTS.has(event)) return null
        const d = { hook_event_name: event, session_id: cut(hook.session_id, 128) ?? '', cwd: cut(hook.cwd, 1024) ?? '' }
        if (event === 'Notification') {
            if (!NOTIFICATIONS.has(hook.notification_type)) return null
            d.notification_type = hook.notification_type
        }
        return d
    }
    return null
}

// Where the tray app writes hook.json; null when this platform has no such place.
const hookFile = () => {
    if (process.env.EDGEPAD_HOOK_FILE) return process.env.EDGEPAD_HOOK_FILE
    if (process.platform === 'win32') {
        return process.env.LOCALAPPDATA ? join(process.env.LOCALAPPDATA, 'Edgepad', 'hook.json') : null
    }
    if (process.platform === 'darwin') return join(homedir(), 'Library', 'Application Support', 'Edgepad', 'hook.json')
    // Linux: only the per-user runtime dir, never a /tmp fallback.
    return process.env.XDG_RUNTIME_DIR ? join(process.env.XDG_RUNTIME_DIR, 'edgepad', 'hook.json') : null
}

const PIPE = /^\\\\\.\\pipe\\edgepad-hook-[0-9a-f]{16}$/

// { endpoint, token } from hook.json, or null. On Unix the file must be a regular file owned by
// this user and not readable or writable by group or others; checked on the open descriptor.
const readHookFile =path => {
    let fd
    try {
        fd = openSync(path, 'r')
        const st = fstatSync(fd)
        if (!st.isFile() || st.size > HOOK_FILE_CAP) return null
        if (process.platform !== 'win32') {
            if (st.uid !== process.getuid() || (st.mode & 0o077) !== 0) return null
        }
        const buf = Buffer.alloc(HOOK_FILE_CAP)
        const n = readSync(fd, buf, 0, HOOK_FILE_CAP, 0)
        const info = JSON.parse(buf.toString('utf8', 0, n))
        if (info?.v !== 1 || typeof info.token !== 'string' || typeof info.endpoint !== 'string') return null
        if (!/^[A-Za-z0-9_-]{16,128}$/.test(info.token)) return null
        const ok = process.platform === 'win32' ? PIPE.test(info.endpoint) : info.endpoint.startsWith('/')
        return ok ? { endpoint: info.endpoint, token: info.token } : null
    } catch {
        return null
    } finally {
        if (fd !== undefined) closeSync(fd)
    }
}

// Reads stdin up to the cap; null when it is longer.
const readStdin = () =>
    new Promise(resolve => {
        const chunks = []
        let size = 0
        process.stdin.on('data', chunk => {
            size += chunk.length
            if (size > STDIN_CAP) {
                resolve(null)
                return
            }
            chunks.push(chunk)
        })
        process.stdin.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')))
        process.stdin.on('error', () => resolve(null))
    })

const parse = text => {
    try {
        return JSON.parse(text)
    } catch {
        return null
    }
}

// Sends one line; for `perm` resolves to the decision ("allow", "deny", "defer") or null, otherwise
// resolves as soon as the line is flushed, without waiting for the reply.
const send = (endpoint, line, waitReply) =>
    new Promise(resolve => {
        const socket = connect(endpoint)
        let reply = Buffer.alloc(0)
        let timer = setTimeout(() => finish(null), CONNECT_MS)
        const finish = value => {
            clearTimeout(timer)
            socket.destroy()
            resolve(value)
        }
        socket.on('error', () => finish(null))
        socket.on('connect', () => {
            clearTimeout(timer)
            if (!waitReply) {
                socket.end(line, () => finish(null))
                timer = setTimeout(() => finish(null), CONNECT_MS)
                return
            }
            timer = setTimeout(() => finish(null), replyWaitMs())
            socket.write(line)
        })
        socket.on('data', chunk => {
            reply = Buffer.concat([reply, chunk])
            const nl = reply.indexOf(10)
            if (nl < 0) {
                if (reply.length > REPLY_CAP) finish(null)
                return
            }
            if (nl > REPLY_CAP) return finish(null)
            const answer = parse(reply.toString('utf8', 0, nl))
            const decision = answer?.v === 1 ? answer.decision : null
            finish(['allow', 'deny', 'defer'].includes(decision) ? decision : null)
        })
        socket.on('end', () => finish(null))
    })

const main = async () => {
    const [agent, kind, arg] = process.argv.slice(2)
    if (!AGENTS.has(agent) || !KINDS.has(kind)) return
    let d
    if (kind === 'perm' || kind === 'session') {
        const text = await readStdin()
        if (text === null) return
        d = buildData(agent, kind, parse(text))
    } else {
        if (typeof arg !== 'string' || Buffer.byteLength(arg) > ARG_CAP) return
        d = parse(arg)
        if (d === null || typeof d !== 'object' || Array.isArray(d)) return
    }
    if (!d) return
    const path = hookFile()
    const info = path && readHookFile(path)
    if (!info) return
    const line = `${JSON.stringify({ v: 1, token: info.token, agent, kind, d })}\n`
    const decision = await send(info.endpoint, line, kind === 'perm')
    if (kind !== 'perm' || (decision !== 'allow' && decision !== 'deny')) return
    const out = { behavior: decision }
    if (decision === 'deny') out.message = DENY_MESSAGE
    const json = JSON.stringify({ hookSpecificOutput: { hookEventName: 'PermissionRequest', decision: out } })
    await new Promise(resolve => process.stdout.write(`${json}\n`, resolve))
}

main().then(quit, quit)
