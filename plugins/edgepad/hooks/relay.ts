// Claude Code mod: forwards the plan limits and context fill to the Edgepad app, run as its own hook client.
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
    // The folder tells the app which session the context fill belongs to; it stays on the laptop.
    // Without it the limits still go: only the context loses its session.
    let cwd: string | undefined
    try {
      cwd = await $.session.cwd()
    } catch {
      cwd = undefined
    }
    const payload = JSON.stringify({
      limits: u.rateLimits.map(l => ({ kind: l.kind, percentUsed: l.percentUsed, resetsAt: l.resetsAt })),
      context: { percent: u.context.percent },
      cwd,
    })
    await $.process.run(
      ["__APP__", '--hook', 'claude', 'usage', payload],
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
