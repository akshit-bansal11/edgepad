// Edgepad OpenCode plugin, version __VERSION__. Written by the Edgepad app (Connect) and removed by it (Disconnect).
// It only tells the app which session started, is working, waits for you, went idle or ended; nothing else leaves OpenCode.
// Follows the https://opencode.ai/docs/en/plugins/ event list. The app never being there must not disturb OpenCode.
declare const Bun: {
  spawn: (cmd: string[], opts: object) => { stdin: { write: (text: string) => void; end: () => void } }
}

type Props = { sessionID?: string; info?: { id?: string }; status?: { type?: string } }

const APP = "__APP__"

const send = (name: string, id: string | undefined, cwd: string) => {
  if (!id) return
  try {
    const p = Bun.spawn([APP, '--hook', 'opencode', 'session'], { stdin: 'pipe', stdout: 'ignore', stderr: 'ignore' })
    p.stdin.write(JSON.stringify({ hook_event_name: name, session_id: id, cwd }))
    p.stdin.end()
  } catch {
    // The app being absent or slow must never disturb the session.
  }
}

export const EdgepadPlugin = async ({ directory }: { directory: string }) => ({
  event: async ({ event }: { event: { type: string; properties?: Props } }) => {
    const p = event.properties ?? {}
    const id = p.sessionID ?? p.info?.id
    switch (event.type) {
      case 'session.created':
      case 'session.idle':
      case 'session.deleted':
      case 'permission.asked':
        send(event.type, id, directory)
        break
      case 'session.status':
        if (p.status?.type === 'busy') send('session.busy', id, directory)
        break
      default:
        break
    }
  },
})
