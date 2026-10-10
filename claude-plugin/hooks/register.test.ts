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

function world(on: On, files: Map<string, string>) {
  mock.env(on, { LOCALAPPDATA: LOCAL })
  on('session.id', () => ({ value: SESSION }))
  // Claude Code's own band beneath ours: an empty box.
  on('ui.render', { component: 'AbovePrompt' }, ($, e) => $.ui.resolve(e).Box({ key: 'engine' }))
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
  test(`${surface}: no status.json draws the offline pill`, async ($, on) => {
    const clock = world(on, new Map())
    const ui = await $.ui.mount({ plugin: 'voxscribe', surface, component: 'AbovePrompt', props: PROPS })
    await clock.advance(600)
    expect((await ui.find({ key: 'vox-pill' }))?.text).toContain('Vox · hors ligne')
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
    expect((await ui.find({ key: 'vox-pill' }))?.text).toContain('Vox · hors ligne')
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
