// Checks the plugin templates: they parse after stamping and use only the known placeholders.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
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
    'marketplace.template.json',
]
const VALUES = {
    __NODE__: 'C:\\Program Files\\nodejs\\node.exe',
    __ROOT__: 'C:\\Users\\a b\\AppData\\Local\\Edgepad\\edgepad',
    __VERSION__: '5.0.0',
}
// Values are substituted JSON-escaped, as the app does.
const stamp = text => text.replace(/__[A-Z]+__/g, m => JSON.stringify(VALUES[m]).slice(1, -1))
const read = f => readFileSync(join(ROOT, f), 'utf8')

test('placeholders used are exactly NODE, ROOT, VERSION', () => {
    const found = new Set()
    for (const f of TEMPLATES) for (const m of read(f).matchAll(/__[A-Z]+__/g)) found.add(m[0])
    assert.deepEqual([...found].sort(), ['__NODE__', '__ROOT__', '__VERSION__'])
})

test('every json template parses after stamping a Windows path', () => {
    for (const f of TEMPLATES.filter(f => f.endsWith('.json'))) {
        assert.doesNotThrow(() => JSON.parse(stamp(read(f))), f)
    }
})

test('stamped hook commands keep the Windows paths whole', () => {
    const claude = JSON.parse(stamp(read('hooks/hooks.claude.json')))
    const h = claude.hooks.PermissionRequest[0].hooks[0]
    assert.equal(h.command, VALUES.__NODE__)
    assert.equal(h.args[0], `${VALUES.__ROOT__}/scripts/edgepad-hook.mjs`)
    const codex = JSON.parse(stamp(read('hooks/hooks.codex.json')))
    assert.equal(
        codex.hooks.PermissionRequest[0].hooks[0].command,
        `"${VALUES.__NODE__}" "${VALUES.__ROOT__}/scripts/edgepad-hook.mjs" codex perm`,
    )
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
