// Checks the plugin templates: they parse after stamping and use only the known placeholders. The hooks run the
// Edgepad app itself (`"<app>" --hook <agent> <kind>`), so the shipped plugin needs no Node; only this test does.
import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { test } from 'node:test'
import { fileURLToPath } from 'node:url'

const ROOT = fileURLToPath(new URL('../edgepad/', import.meta.url))
const TEMPLATES = [
    '.claude-plugin/plugin.json',
    '.codex-plugin/plugin.json',
    'hooks/hooks.claude.json',
    'hooks/hooks.codex.json',
    'hooks/relay.ts',
    'hooks/hooks.gemini.json',
    'opencode/edgepad.ts',
    'marketplace.template.json',
]
const VALUES = {
    __APP__: String.raw`C:\Users\a b\AppData\Local\Programs\Edgepad\Edgepad.exe`,
    __VERSION__: '5.0.0',
}
// Values are substituted JSON-escaped, as the app does.
const stamp = text => text.replace(/__[A-Z]+__/g, m => JSON.stringify(VALUES[m]).slice(1, -1))
const read = f => readFileSync(join(ROOT, f), 'utf8')

test('placeholders used are exactly APP and VERSION', () => {
    const found = new Set()
    for (const f of TEMPLATES) for (const m of read(f).matchAll(/__[A-Z]+__/g)) found.add(m[0])
    assert.deepEqual([...found].sort(), ['__APP__', '__VERSION__'])
})

test('the plugin ships no scripts', () => {
    const files = readdirSync(ROOT, { recursive: true }).map(f => f.replaceAll(String.fromCharCode(92), '/'))
    assert.deepEqual(files.filter(f => /\.(m?js|cjs|sh|py)$/.test(f)), [])
})

test('every json template parses after stamping a Windows path', () => {
    for (const f of TEMPLATES.filter(f => f.endsWith('.json'))) {
        assert.doesNotThrow(() => JSON.parse(stamp(read(f))), f)
    }
})

test('every hook runs the app with --hook, its agent and a known kind', () => {
    const claude = JSON.parse(stamp(read('hooks/hooks.claude.json')))
    for (const groups of Object.values(claude.hooks)) {
        for (const h of groups.flatMap(g => g.hooks)) {
            assert.equal(h.command, VALUES.__APP__)
            assert.equal(h.args.length, 3)
            assert.deepEqual(h.args.slice(0, 2), ['--hook', 'claude'])
            assert.ok(['perm', 'session'].includes(h.args[2]))
        }
    }
    assert.deepEqual(claude.hooks.PermissionRequest[0].hooks[0].args, ['--hook', 'claude', 'perm'])
    const codex = JSON.parse(stamp(read('hooks/hooks.codex.json')))
    assert.equal(codex.hooks.PermissionRequest[0].hooks[0].command, `"${VALUES.__APP__}" --hook codex perm`)
    assert.equal(codex.hooks.SessionStart[0].hooks[0].command, `"${VALUES.__APP__}" --hook codex session`)
})

test('relay.ts runs the app for usage', () => {
    assert.match(read('hooks/relay.ts'), /\["__APP__", '--hook', 'claude', 'usage', payload\]/)
})

test('permission hooks match all tools; codex has no modules and only two events', () => {
    const claude = JSON.parse(read('hooks/hooks.claude.json'))
    const codex = JSON.parse(read('hooks/hooks.codex.json'))
    for (const j of [claude, codex]) assert.ok(['*', '', undefined].includes(j.hooks.PermissionRequest[0].matcher))
    assert.equal(codex.modules, undefined)
    assert.deepEqual(Object.keys(codex.hooks).sort(), ['PermissionRequest', 'SessionStart'])
    assert.deepEqual(claude.modules, ['./relay.ts'])
})

test('relay.ts imports only types', () => {
    const lines = read('hooks/relay.ts').split('\n').filter(l => /^\s*import\b/.test(l))
    assert.ok(lines.length > 0)
    for (const line of lines) assert.match(line, /^import type /)
})

// Claude Code and Codex wait on a hook that is not async, so every event but the approval runs in the background:
// starting the app must never stall a prompt, a reply or a session.
test('only the permission hooks are waited on', () => {
    for (const f of ['hooks/hooks.claude.json', 'hooks/hooks.codex.json']) {
        for (const [event, groups] of Object.entries(JSON.parse(read(f)).hooks)) {
            for (const h of groups.flatMap(g => g.hooks)) {
                assert.equal(h.async === true, event !== 'PermissionRequest', `${f} ${event}`)
            }
        }
    }
})

// Gemini CLI: five hook entries, all named edgepad, all the same session command; no approvals (Gemini is board-only).
test('gemini hooks run the app for sessions only and carry our name', () => {
    const gemini = JSON.parse(stamp(read('hooks/hooks.gemini.json')))
    assert.deepEqual(Object.keys(gemini.hooks).sort(), ['AfterAgent', 'BeforeAgent', 'Notification', 'SessionEnd', 'SessionStart'])
    for (const groups of Object.values(gemini.hooks)) {
        for (const h of groups.flatMap(g => g.hooks)) {
            assert.equal(h.name, 'edgepad')
            assert.equal(h.type, 'command')
            assert.equal(h.command, `"${VALUES.__APP__}" --hook gemini session`)
            assert.ok(h.timeout > 0)
        }
    }
})

// OpenCode: a plugin file the app writes whole; its first line is the marker the installer checks before it ever deletes the file.
test('opencode plugin forwards session events to the app and is marked as ours', () => {
    const text = read('opencode/edgepad.ts')
    assert.match(text.split('\n')[0], /Edgepad OpenCode plugin/)
    assert.match(stamp(text), /const APP = "C:\\Users\\a b\\AppData\\Local\\Programs\\Edgepad\\Edgepad\.exe"/)
    assert.match(text, /'--hook', 'opencode', 'session'/)
    for (const name of ['session.created', 'session.idle', 'session.deleted', 'permission.asked', 'session.busy']) {
        assert.ok(text.includes(name), name)
    }
})

// The claude and codex copies must not carry the gemini or opencode files: stamping is done by the C# side's Target rule,
// so here only the file names are pinned for it.
test('the gemini and opencode templates sit where the installer looks for them', () => {
    const files = readdirSync(ROOT, { recursive: true }).map(f => f.replaceAll(String.fromCharCode(92), '/'))
    assert.ok(files.includes('hooks/hooks.gemini.json'))
    assert.ok(files.includes('opencode/edgepad.ts'))
})
