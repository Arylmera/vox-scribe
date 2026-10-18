import type { On } from 'claude-code'
import { expect, mock, test } from 'claude-code/testing'

// The band drawn from the app's files, which the test plays: a map of path to text beneath $.fs.
const SURFACES = ['terminal', 'desktop'] as const
const LOCAL = 'C:/Users/test/AppData/Local'
const DIR = `${LOCAL}/VoxScribe/claude`
const T0 = 1_700_000_000_000
const SESSION = 'session-a'
const PROPS = {
  hasSurvey: false,
  isWorking: false,
  maxRows: 10,
  bodyColumns: 120,
  scroll: { offset: 0, bodyRows: 10 },
  view: {},
}

/** The engine hands $.fs a Windows path; the test keys its files with forward slashes. */
const slash = (path: string) => path.replace(/\\/g, '/')

function world(on: On, files: Map<string, string>, cogitator = false) {
  mock.env(on, { LOCALAPPDATA: LOCAL })
  on('session.id', () => ({ value: SESSION }))
  // Beneath ours: Claude Code's own band, an empty box; or Cogitator's, its column beside a side column for the others.
  on('ui.render', { component: 'AbovePrompt' }, ($, e) => {
    const { Box, Text } = $.ui.resolve(e)
    if (!cogitator) return Box({ key: 'engine' })
    return Box({
      key: 'cogitator',
      flexDirection: 'row',
      children: [Box({ key: 'cogitator-row', children: [Text({ children: 'kev prêt' })] }), Box({ key: 'cg-beside', flexDirection: 'column', children: [Box({ key: 'gauges' })] })],
    } as never)
  })
  on('fs.read', ($, e) => {
    const text = files.get(slash(e.path))
    return text === undefined ? { deny: `ENOENT ${e.path}` } : { value: text }
  })
  on('fs.write', ($, e) => {
    files.set(slash(e.path), e.text)
    return { value: undefined }
  })
  return mock.clock(on, { now: T0 })
}

for (const surface of SURFACES) {
  test(`${surface}: no status.json draws no band`, async ($, on) => {
    const clock = world(on, new Map())
    const ui = await $.ui.mount({ plugin: 'voxscribe', surface, component: 'AbovePrompt', props: PROPS })
    await clock.advance(600)
    expect(await ui.find({ key: 'vox-band' })).toBeFalsy()
    expect(await ui.findAll({ type: 'Button' })).toHaveLength(0)
  })

  test(`${surface}: a fresh status draws the ready pill and four buttons`, async ($, on) => {
    const files = new Map([[`${DIR}/status.json`, JSON.stringify({ state: 'idle', session_id: null, since: T0, beat: T0 })]])
    const clock = world(on, files)
    const ui = await $.ui.mount({ plugin: 'voxscribe', surface, component: 'AbovePrompt', props: PROPS })
    await clock.advance(600)
    expect((await ui.find({ key: 'vox-pill' }))?.text).toContain('Vox · prêt')
    const keys = (await ui.findAll({ type: 'Button' })).map(b => b.key)
    expect(keys).toEqual(['vox-arm', 'vox-talk', 'vox-fr', 'vox-en'])

    await clock.advance(6000) // the app stopped beating
    expect(await ui.find({ key: 'vox-band' })).toBeFalsy()

    files.set(`${DIR}/status.json`, JSON.stringify({ state: 'idle', session_id: null, since: clock.now(), beat: clock.now() }))
    await clock.advance(600) // the app is back
    expect((await ui.find({ key: 'vox-pill' }))?.text).toContain('Vox · prêt')
  })
}

test('an outbox for this session is submitted once and acknowledged', async ($, on) => {
  const files = new Map<string, string>()
  const clock = world(on, files)
  const submitted: string[] = []
  on('prompt.submit', ($, e) => {
    submitted.push(e.text)
    return new Promise(() => undefined) // busy: resolves only when the queued turn is sent
  })
  files.set(`${DIR}/status.json`, JSON.stringify({ state: 'idle', session_id: null, since: T0, beat: T0 }))
  files.set(`${DIR}/outbox/${SESSION}.json`, JSON.stringify({ id: 'abc', text: 'bonjour', ts: T0 }))

  await $.ui.mount({ plugin: 'voxscribe', surface: 'terminal', component: 'AbovePrompt', props: PROPS })
  await clock.advance(1500)

  expect(submitted).toEqual(['bonjour'])
  expect(files.has(`${DIR}/outbox/abc.ack`)).toBe(true)
})

test('a stale outbox (older than 6s) is never submitted and not acked', async ($, on) => {
  const files = new Map<string, string>()
  const clock = world(on, files)
  const submitted: string[] = []
  on('prompt.submit', ($, e) => {
    submitted.push(e.text)
    return new Promise(() => undefined)
  })
  files.set(`${DIR}/status.json`, JSON.stringify({ state: 'idle', session_id: null, since: T0, beat: T0 }))
  // Already older than STALE_MS (6000ms) the moment the first poll reads it.
  files.set(`${DIR}/outbox/${SESSION}.json`, JSON.stringify({ id: 'stale', text: 'bonjour', ts: T0 - 7000 }))

  await $.ui.mount({ plugin: 'voxscribe', surface: 'terminal', component: 'AbovePrompt', props: PROPS })
  await clock.advance(1500)

  expect(submitted).toEqual([])
  expect(files.has(`${DIR}/outbox/stale.ack`)).toBe(false)
})

test('an oversized outbox text is never submitted', async ($, on) => {
  const files = new Map<string, string>()
  const clock = world(on, files)
  const submitted: string[] = []
  on('prompt.submit', ($, e) => {
    submitted.push(e.text)
    return new Promise(() => undefined)
  })
  files.set(`${DIR}/status.json`, JSON.stringify({ state: 'idle', session_id: null, since: T0, beat: T0 }))
  files.set(`${DIR}/outbox/${SESSION}.json`, JSON.stringify({ id: 'big', text: 'x'.repeat(40_000), ts: T0 }))

  await $.ui.mount({ plugin: 'voxscribe', surface: 'terminal', component: 'AbovePrompt', props: PROPS })
  await clock.advance(1500)

  expect(submitted).toEqual([])
  expect(files.has(`${DIR}/outbox/big.ack`)).toBe(false)
})

test('a blank outbox text is never submitted', async ($, on) => {
  const files = new Map<string, string>()
  const clock = world(on, files)
  const submitted: string[] = []
  on('prompt.submit', ($, e) => {
    submitted.push(e.text)
    return new Promise(() => undefined)
  })
  files.set(`${DIR}/status.json`, JSON.stringify({ state: 'idle', session_id: null, since: T0, beat: T0 }))
  files.set(`${DIR}/outbox/${SESSION}.json`, JSON.stringify({ id: 'blank', text: '   ', ts: T0 }))

  await $.ui.mount({ plugin: 'voxscribe', surface: 'terminal', component: 'AbovePrompt', props: PROPS })
  await clock.advance(1500)

  expect(submitted).toEqual([])
  expect(files.has(`${DIR}/outbox/blank.ack`)).toBe(false)
})

test('cibler arms this session until another one takes the target; parler asks for the mic', async ($, on) => {
  const files = new Map([[`${DIR}/status.json`, JSON.stringify({ state: 'idle', session_id: null, since: T0, beat: T0 })]])
  const clock = world(on, files)
  const ui = await $.ui.mount({ plugin: 'voxscribe', surface: 'terminal', component: 'AbovePrompt', props: PROPS })
  await clock.advance(600)

  await ui.press({ key: 'vox-arm' })
  expect((await ui.find({ key: 'vox-pill' }))?.text).toContain('Vox · cible')
  expect(JSON.parse(files.get(`${DIR}/target.json`) ?? '{}').session_id).toBe(SESSION)

  await clock.advance(2400) // polls and a heartbeat later, still armed
  expect((await ui.find({ key: 'vox-pill' }))?.text).toContain('Vox · cible')

  files.set(`${DIR}/target.json`, JSON.stringify({ session_id: 'session-b', beat: clock.now() }))
  await clock.advance(600)
  expect((await ui.find({ key: 'vox-pill' }))?.text).toContain('Vox · prêt')

  await ui.press({ key: 'vox-talk' })
  expect(JSON.parse(files.get(`${DIR}/mic.json`) ?? '{}')).toMatchObject({ session_id: SESSION, action: 'start' })
  expect(JSON.parse(files.get(`${DIR}/target.json`) ?? '{}').session_id).toBe(SESSION)
})

test('desktop draws the Instrument band borderless: a coloured dot and word, plain buttons, the target lit', async ($, on) => {
  const files = new Map([[`${DIR}/status.json`, JSON.stringify({ state: 'idle', session_id: null, since: T0, beat: T0 })]])
  const clock = world(on, files)
  const ui = await $.ui.mount({ plugin: 'voxscribe', surface: 'desktop', component: 'AbovePrompt', props: PROPS })
  await clock.advance(600)

  // The band's divider is its only rule: no box of Vox draws a border.
  for (const key of ['vox-pill', 'vox-dictation', 'vox-read']) {
    expect((await ui.find({ key }))?.props?.borderStyle).toBeUndefined()
  }
  const dot = async () => JSON.stringify(await ui.drawn()).match(/"color":"(#[0-9a-f]{6})"\},"children":\["[◎◉●]"\]/)?.[1]
  expect(await dot()).toBe('#a9dc76')
  expect((await ui.find({ key: 'vox-arm' }))?.props?.plain).toBe(true)

  await ui.press({ key: 'vox-arm' })
  expect(await dot()).toBe('#78dce8')
  expect((await ui.find({ key: 'vox-arm' }))?.props?.variant).toBe('primary')

  files.set(`${DIR}/status.json`, JSON.stringify({ state: 'listening', session_id: SESSION, since: clock.now(), beat: clock.now() }))
  await clock.advance(600)
  expect(await dot()).toBe('#ff6188')
  expect((await ui.find({ key: 'vox-mic' }))?.props?.borderStyle).toBeUndefined()
  expect((await ui.find({ key: 'vox-send' }))?.props?.variant).toBe('primary')
})

test('terminal keeps its tinted pill and loose plain buttons', async ($, on) => {
  const files = new Map([[`${DIR}/status.json`, JSON.stringify({ state: 'idle', session_id: null, since: T0, beat: T0 })]])
  const clock = world(on, files)
  const ui = await $.ui.mount({ plugin: 'voxscribe', surface: 'terminal', component: 'AbovePrompt', props: PROPS })
  await clock.advance(600)

  const pill = (await ui.find({ key: 'vox-pill' }))?.props
  expect(pill?.backgroundColor).toBe('#2A2A2A')
  expect(pill?.borderStyle).toBeUndefined()
  expect(await ui.find({ key: 'vox-dictation' })).toBeFalsy()
  expect((await ui.find({ key: 'vox-arm' }))?.props?.plain).toBe(true)
})

for (const surface of SURFACES) {
  test(`${surface}: under Cogitator the Vox row sits at the foot of its side column, under the gauges`, async ($, on) => {
    const files = new Map([[`${DIR}/status.json`, JSON.stringify({ state: 'idle', session_id: null, since: T0, beat: T0 })]])
    const clock = world(on, files, true)
    const ui = await $.ui.mount({ plugin: 'voxscribe', surface, component: 'AbovePrompt', props: PROPS })
    await clock.advance(600)
    type N = { props?: { key?: string }; children?: unknown[] }
    const tree = (await ui.drawn()) as N
    // No column of Vox's own around the band: Cogitator's row is the root, Vox inside its side column, last.
    expect(tree.props?.key).toBe('cogitator')
    const beside = (tree.children as N[]).find(c => c?.props?.key === 'cg-beside')
    expect((beside?.children as N[]).map(c => c?.props?.key)).toEqual(['gauges', 'vox-band'])
    expect(await ui.find({ key: 'vox-arm' })).toBeTruthy()
  })
}
