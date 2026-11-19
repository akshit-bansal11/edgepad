// Claude Code mod: forwards the plan limits and context fill to the Edgepad app through the hook client.
// No UI, atoms or commands. The laptop maps the limit kinds; this only forwards what Claude reports.
import type { EngineInterface, Register } from 'claude-code'

const MIN_GAP_MS = 30_000
let lastSent = 0

const send = async ($: EngineInterface, force: boolean) => {
  try {
    const now = Date.now()
    if (!force && now - lastSent < MIN_GAP_MS) return
    lastSent = now
    const u = await $.session.usage()
    const payload = JSON.stringify({
      limits: u.rateLimits.map(l => ({ kind: l.kind, percentUsed: l.percentUsed, resetsAt: l.resetsAt })),
      context: { percent: u.context.percent },
    })
    await $.process.run(
      ["__NODE__", `${$.plugin.root}/scripts/edgepad-hook.mjs`, 'claude', 'usage', payload],
      { timeoutMs: 5000 },
    )
  } catch {
    // The app being absent or slow must never disturb the session.
  }
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    void send($, true)
    return next(e)
  })
  on('session.measure', async ($, e, next) => {
    void send($, false)
    return next(e)
  })
  on('turn.complete', async ($, e, next) => {
    void send($, false)
    return next(e)
  })
}
