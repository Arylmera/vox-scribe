import { atom, read, update } from 'claude-code'
import type { EngineInterface, Register } from 'claude-code'

import type { VoxBand } from '../types'

// The Vox-Scribe band above Claude Code's prompt. Deliberately dumb: it draws, writes intent
// files and submits what the app leaves in this session's outbox. Every rule (which session is
// live, acknowledgement, fallback) lives in the app (ClaudeBridge.cs), where it is tested.
// Every handler catches: if this breaks, the band goes away and Claude Code does not notice.

const TICK_MS = 150 // the waveform's frame; the files are read every other tick
const BEAT_MS = 2000
const STALE_MS = 6000
const MAX_TEXT = 32_000
const MAX_FILE = 64_000
const TRIP_AFTER = 20
const DEFAULT_ACCENT = '#3DDC84'
const RECORDING = '#E5484D' // red means recording, and nothing else
const RECORDING_BG = '#3A1517'
const MUTED_BG = '#2A2A2A'
const WAVE = '▁▂▃▄▅▆▇'
const SPIN = '⠁⠂⠄⡀⢀⠠⠐⠈'

const band = atom({ plugin: 'voxscribe', key: 'band' } as const, {
  kind: 'offline',
  armed: false,
  since: 0,
  accent: DEFAULT_ACCENT,
} as VoxBand)
const now = atom({ plugin: 'voxscribe', key: 'now' } as const, 0)
const lastHandled = atom({ plugin: 'voxscribe', key: 'lastHandled' } as const, null as string | null)

// Not drawn, so module variables: a reload starting them over costs one early beat at most.
let dir: string | null = null
let sid: string | null = null
let lastBeat = 0
let errors = 0
let ticks = 0
let polling = false
let presses = 0 // bumped at each click, before any await: a poll that saw one change keeps its hands off `armed`
let timer: { cancel: () => void } | null = null

async function base($: EngineInterface): Promise<string> {
  if (dir) return dir
  const local = await $.env.get('LOCALAPPDATA')
  if (!local) throw new Error('LOCALAPPDATA is not set')
  return (dir = `${local}/VoxScribe/claude`)
}

async function me($: EngineInterface): Promise<string> {
  return (sid ??= await $.session.id())
}

async function readJson($: EngineInterface, path: string): Promise<any | null> {
  const text = await $.fs.read(path).catch(() => '')
  if (typeof text !== 'string' || text.length === 0 || text.length > MAX_FILE) return null
  try {
    return JSON.parse(text)
  } catch {
    return null
  }
}

async function writeTarget($: EngineInterface, session: string | null) {
  lastBeat = await $.clock.now()
  await $.fs.write(`${await base($)}/target.json`, JSON.stringify({ session_id: session, beat: session ? lastBeat : 0 }))
}

async function writeMic($: EngineInterface, action: 'start' | 'stop' | 'cancel') {
  const ts = await $.clock.now()
  await $.fs.write(`${await base($)}/mic.json`, JSON.stringify({ session_id: await me($), action, ts }))
}

/** The one place the prompt is touched: a fresh, sane outbox for this session, at most once. */
async function drainOutbox($: EngineInterface, d: string, session: string, t: number) {
  const o = await readJson($, `${d}/outbox/${session}.json`)
  if (!o || typeof o.id !== 'string' || typeof o.text !== 'string' || typeof o.ts !== 'number') return
  // Test-and-set before submitting: at most once, never twice, even with two polls in flight.
  let fresh = false
  await update($, lastHandled, v => {
    fresh = v !== o.id
    return o.id
  })
  if (!fresh) return
  if (t - o.ts > STALE_MS || o.text.trim() === '' || o.text.length > MAX_TEXT) return
  // Spike Q3b: while Claude is busy, submit resolves only when the queued message is sent, so
  // the ack cannot wait for it. An early rejection (no session bound yet) means not sent: give
  // it a moment to reject, then acknowledge. The app's own timeout reports a rejection.
  const sent = $.prompt.submit({ text: o.text })
  const early = await Promise.race([
    sent.then(
      () => 'ok',
      () => 'rejected',
    ),
    $.clock.sleep(500).then(() => 'pending'),
  ])
  if (early === 'rejected') return
  void sent.catch(() => undefined)
  await $.fs.write(`${d}/outbox/${o.id}.ack`, '')
}

/** The accent, deep: the pill's ground (mixed with black, ~75 %, matching the approved demo). */
function deep(hex: string): string {
  const n = parseInt(hex.slice(1), 16)
  const c = (shift: number) => Math.round(((n >> shift) & 0xff) * 0.25).toString(16).padStart(2, '0')
  return `#${c(16)}${c(8)}${c(0)}`
}

async function poll($: EngineInterface, t: number) {
  const d = await base($)
  const session = await me($)
  const seen = presses
  const target = await readJson($, `${d}/target.json`)
  const was = await read($, band)
  // Another session took the target, unless a click here raced the read.
  const taken = target?.session_id !== session
  const armedNow = (cur: VoxBand) => cur.armed && !(taken && presses === seen)
  const armed = armedNow(was)
  if (armed && t - lastBeat >= BEAT_MS) await writeTarget($, session)

  const s = await readJson($, `${d}/status.json`)
  const alive = !!s && typeof s.beat === 'number' && typeof s.state === 'string' && t - s.beat <= STALE_MS
  const mine = alive && s.session_id === session
  const accent = alive && /^#[0-9A-Fa-f]{6}$/.test(s.accent_dark) ? s.accent_dark : was.accent
  const kind: VoxBand['kind'] = !alive
    ? 'offline'
    : mine && (s.state === 'listening' || s.state === 'transcribing')
      ? s.state
      : 'ready'
  const next: VoxBand = { kind, armed, since: kind === 'listening' ? Number(s.since) || t : 0, accent }
  if (JSON.stringify(next) !== JSON.stringify(was)) await update($, band, cur => ({ ...next, armed: armedNow(cur) }))

  await drainOutbox($, d, session, t)
}

async function tick($: EngineInterface) {
  // The whole body sits in the try: a failure of clock, read or update must trip the breaker
  // too, not just a failure inside poll.
  try {
    const t = await $.clock.now()
    const shown = await read($, band)
    if (shown.kind === 'listening' || shown.kind === 'transcribing') await update($, now, () => t)
    if (ticks++ % 2 !== 0 || polling) return
    polling = true
    try {
      await poll($, t)
    } finally {
      polling = false
    }
    errors = 0
  } catch {
    if (++errors >= TRIP_AFTER) {
      timer?.cancel()
      await update($, band, (b): VoxBand => ({ ...b, kind: 'broken' }))
    }
  }
}

function startTimer($: EngineInterface) {
  if (timer) return
  timer = $.clock.every(TICK_MS, () => void tick($).catch(() => undefined))
}

/** A button's work, then a poll so the band answers at once; never throws into Claude Code. */
function press($: EngineInterface, run: () => Promise<unknown>) {
  return () => {
    presses++
    void run()
      .then(async () => poll($, await $.clock.now()))
      .catch(() => undefined)
  }
}

function wave(t: number) {
  const n = Math.floor(t / TICK_MS)
  let s = ''
  for (let i = 0; i < 10; i++) s += WAVE[Math.floor((Math.sin(n * 0.7 + i * 0.9) + 1) * 3)]
  return s
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    // A timer started inside a request may end with it; the session start owns it.
    try {
      sid = await $.session.id()
      startTimer($)
    } catch {
      // No band this session; Claude Code carries on.
    }
    return next(e)
  })

  on('ui.render', { component: 'AbovePrompt' }, async ($, e, next) => {
    // `next(e)` runs exactly once, however the try below ends: computing `ours` throwing must
    // never cause it to run a second time.
    let ours: JSX.Element | null = null
    const { Box, Text, Button } = $.ui.resolve(e)
    try {
      startTimer($) // a plugin reload may draw before any session start
      const b = await read($, band)
      const busy = b.kind === 'listening' || b.kind === 'transcribing'
      const t = busy ? await read($, now) : 0
      const ground = deep(b.accent)

      // One row: a tinted pill, no border, so it sits level with the buttons.
      const pill = (dot: string, dotColor: string, label: string, bg: string, extra?: JSX.Element) => (
        <Box key="vox-pill" backgroundColor={bg} paddingX={1} flexDirection="row" gap={1}>
          <Text color={dotColor}>{dot}</Text>
          <Text color="white">{label}</Text>
          {extra}
        </Box>
      )

      if (b.kind === 'offline' || b.kind === 'broken') {
        ours = (
          <Box key="vox-band" flexDirection="row" gap={1} alignItems="center">
            {pill('○', 'gray', b.kind === 'broken' ? 'Vox · désactivé (erreur)' : 'Vox · hors ligne', MUTED_BG)}
            {b.kind === 'offline' ? <Text dimColor>lance Vox-Scribe pour dicter ici</Text> : null}
          </Box>
        )
      } else if (b.kind === 'listening') {
        const secs = Math.max(0, Math.floor((t - b.since) / 1000))
        const clock = `${Math.floor(secs / 60)}:${String(secs % 60).padStart(2, '0')}`
        ours = (
          <Box key="vox-band" flexDirection="row" gap={1} alignItems="center">
            {pill('●', RECORDING, clock, RECORDING_BG, <Text color={b.accent}>{wave(t)}</Text>)}
            <Button key="vox-send" variant="primary" label="■ envoyer" onPress={press($, () => writeMic($, 'stop'))} />
            <Button key="vox-cancel" plain dimColor label="✕ annuler" onPress={press($, () => writeMic($, 'cancel'))} />
          </Box>
        )
      } else if (b.kind === 'transcribing') {
        ours = (
          <Box key="vox-band" flexDirection="row" gap={1} alignItems="center">
            {pill(SPIN.charAt(Math.floor(t / TICK_MS) % SPIN.length), b.accent, 'Vox · transcription…', ground)}
          </Box>
        )
      } else {
        ours = (
          <Box key="vox-band" flexDirection="row" gap={2} alignItems="center">
            {pill(b.armed ? '◉' : '◎', b.accent, b.armed ? 'Vox · cible' : 'Vox · prêt', b.armed ? ground : MUTED_BG)}
            <Button
              key="vox-arm"
              plain
              dimColor={!b.armed}
              label={b.armed ? '🎯 désarmer' : '🎯 cibler'}
              onPress={press($, async () => {
                const v = await update($, band, x => ({ ...x, armed: !x.armed }))
                await writeTarget($, v.armed ? await me($) : null)
              })}
            />
            <Button
              key="vox-talk"
              plain
              label="🎙 parler"
              onPress={press($, async () => {
                await update($, band, x => ({ ...x, armed: true }))
                await writeTarget($, await me($))
                await writeMic($, 'start')
              })}
            />
            <Button key="vox-fr" plain dimColor label="🔊 fr" onPress={press($, () => $.command.run({ command: 'voxscribe:parle', args: '' }))} />
            <Button key="vox-en" plain dimColor label="🔊 en" onPress={press($, () => $.command.run({ command: 'voxscribe:say', args: '' }))} />
          </Box>
        )
      }
    } catch {
      ours = null
    }
    const rest = await next(e)
    if (!ours) return rest
    return (
      <Box key="vox-col" flexDirection="column">
        {ours}
        {rest}
      </Box>
    )
  })
}
