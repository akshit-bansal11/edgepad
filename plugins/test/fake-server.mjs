// A stand-in for the tray app's hook listener, for the plugin tests: a Unix socket (or, on
// Windows, a named pipe) plus the hook.json that points at it. Importing this file starts nothing.
import { randomBytes } from 'node:crypto'
import { chmodSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { createServer } from 'node:net'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

export const TOKEN = randomBytes(32).toString('base64url')

// reply: the line to answer each request with (a "\n" is added), or null to never answer.
// delayMs: wait this long before answering.
export const startServer = async ({ reply = null, delayMs = 0 } = {}) => {
    const dir = mkdtempSync(join(tmpdir(), 'edgepad-hook-'))
    const endpoint =
        process.platform === 'win32'
            ? `\\\\.\\pipe\\edgepad-hook-${randomBytes(8).toString('hex')}`
            : join(dir, 'hook.sock')
    const file = join(dir, 'hook.json')
    const lines = []
    const sockets = new Set()
    const state = { connections: 0, replied: false }
    const waiters = []
    const server = createServer(socket => {
        state.connections++
        sockets.add(socket)
        socket.on('close', () => sockets.delete(socket))
        socket.on('error', () => {})
        let buf = Buffer.alloc(0)
        socket.on('data', chunk => {
            buf = Buffer.concat([buf, chunk])
            const nl = buf.indexOf(10)
            if (nl < 0) return
            lines.push(buf.toString('utf8', 0, nl))
            buf = Buffer.alloc(0)
            for (const w of waiters.splice(0)) w()
            if (reply === null) return
            setTimeout(() => {
                if (socket.destroyed) return
                state.replied = true
                socket.end(`${reply}\n`)
            }, delayMs).unref()
        })
    })
    await new Promise((resolve, reject) => {
        server.once('error', reject)
        server.listen(endpoint, resolve)
    })
    writeHookFile(file, endpoint)
    return {
        dir,
        file,
        endpoint,
        lines,
        state,
        // Resolves once a line has arrived, or after ms either way.
        waitLine: (ms = 3000) =>
            lines.length
                ? Promise.resolve()
                : new Promise(resolve => {
                      waiters.push(resolve)
                      setTimeout(resolve, ms).unref()
                  }),
        close: async () => {
            for (const s of sockets) s.destroy()
            await new Promise(resolve => server.close(resolve))
            rmSync(dir, { recursive: true, force: true })
        },
    }
}

export const writeHookFile = (file, endpoint, token = TOKEN) => {
    writeFileSync(file, JSON.stringify({ v: 1, endpoint, token, pid: process.pid }), { mode: 0o600 })
    if (process.platform !== 'win32') chmodSync(file, 0o600)
}
