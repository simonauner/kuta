import assert from 'node:assert/strict'
import { test } from 'node:test'
import { partStreams } from '../src/lib/multisport.ts'
import { formatSportPace } from '../src/lib/activity.ts'

test('cut endpoints interpolate channels and rebase time and distance for splits', () => {
  const streams = {
    t: [0, 100, 200],
    dist: [0, 1000, 2000],
    speed: [10, 10, 10],
    hr: [100, null, 150],
    lat: [59, 60, 61],
    lon: [17, 18, 19],
    alt: null,
    cadence: null,
    temp: null,
  }
  const part = partStreams(streams, { startS: 50, endS: 150 })
  assert.deepEqual(part.t, [0, 50, 100])
  assert.deepEqual(part.dist, [0, 500, 1000])
  assert.deepEqual(part.lat, [59.5, 60, 60.5])
  assert.deepEqual(part.hr, [null, null, null])
  assert.equal(part.alt, null)
  assert.deepEqual(streams.dist, [0, 1000, 2000])
})

test('sport presentation uses meaningful units and omits aggregate multisport pace', () => {
  assert.equal(formatSportPace('swimming', 600, 1000, 'en'), '1:00 /100 m')
  assert.equal(formatSportPace('cycling', 3600, 20500, 'sv'), '20,5 km/h')
  assert.equal(formatSportPace('running', 300, 1000, 'en'), '5:00 /km')
  assert.equal(formatSportPace('swimrun', 600, 1000, 'en'), '–')
  assert.equal(formatSportPace('swimming', 600, 0, 'en'), '–')
})

test('draft sport legs are previewed locally and restoring uses the imported version', async () => {
  const { multisportView } = await import('../src/lib/multisport.ts')
  const original = {
    sport: 'swimrun',
    parts: [{ partIndex: 0 }],
    partsEdited: true,
    originalParts: [],
    originalSport: 'running',
  }
  assert.equal(multisportView(original, undefined), original)
  const draft = { kind: 'triathlon', parts: [{ partIndex: 1 }] }
  assert.equal(multisportView(original, draft).sport, 'triathlon')
  assert.deepEqual(multisportView(original, draft).parts, draft.parts)
  const restored = multisportView(original, null)
  assert.equal(restored.sport, 'running')
  assert.deepEqual(restored.parts, [])
  assert.equal(restored.partsEdited, false)
  assert.equal(original.sport, 'swimrun')
})
