import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  activityTitle,
  formatDuration,
  formatPace,
  parseDuration,
  splits,
  toDateTimeLocal,
} from '../src/lib/activity.ts'

// 2,5 km på 750 s: 3 m/s, en mätpunkt per sekund, höjden stiger 1 m per 100 m, puls 150.
function run({ seconds = 834, gapAt = null } = {}) {
  const t = Array.from({ length: seconds + 1 }, (_, i) => i)
  return {
    t,
    dist: t.map((i) => (i === gapAt ? null : i * 3)),
    alt: t.map((i) => 10 + (i * 3) / 100),
    hr: t.map(() => 150),
    speed: null,
    lat: null,
    lon: null,
  }
}

test('splits per full kilometre plus the remainder', () => {
  const result = splits(run())
  assert.deepEqual(
    result.map((s) => [s.km, s.distanceM, s.durationS]),
    [
      // Varje split börjar där förra slutade: vid första punkten över gränsen
      [1, 1002, 334],
      [2, 999, 333],
      [3, 501, 167],
    ],
  )
  assert.equal(result[0].elevationM, 10.02)
  assert.equal(result[0].avgHr, 150)
})

test('a few metres after the last kilometre are noise, not a split', () => {
  assert.deepEqual(
    splits(run({ seconds: 340 })).map((s) => s.km),
    [1],
  )
})

test('missing distance points are skipped, and no distance means no splits', () => {
  assert.equal(splits(run({ gapAt: 334 }))[0].durationS, 335)
  assert.deepEqual(splits({ ...run(), dist: null }), [])
})

test('durations and paces', () => {
  assert.equal(formatDuration(95), '1:35')
  assert.equal(formatDuration(3725), '1:02:05')
  assert.equal(formatPace(1500, 5000), '5:00 /km')
  assert.equal(formatPace(10, 0), '–')
})

test('own title wins, otherwise sport and time of day', () => {
  const t = (key, values) => (values ? `${values.sport}|${values.timeOfDay}` : key.split('.').pop())
  const at = (hour) => new Date(2026, 9, 30, hour, 0).toISOString()
  assert.equal(
    activityTitle({ title: 'Sörmlandsleden', sport: 'running', startedAt: at(15) }, t),
    'Sörmlandsleden',
  )
  assert.equal(
    activityTitle({ title: null, sport: 'running', startedAt: at(15) }, t),
    'running|afternoon',
  )
  assert.equal(
    activityTitle({ title: null, sport: 'rowing', startedAt: at(6) }, t),
    'other|morning',
  )
  assert.equal(
    activityTitle({ title: null, sport: 'running', startedAt: at(23) }, t),
    'running|night',
  )
})

test('durations are read as h:mm:ss, mm:ss or whole minutes', () => {
  assert.equal(parseDuration('1:02:30'), 3750)
  assert.equal(parseDuration(' 45:00 '), 2700)
  assert.equal(parseDuration('45'), 2700)
  for (const invalid of ['', '0', '1:60', '1:2:3:4', '4,5', '-5'])
    assert.equal(parseDuration(invalid), null)
})

test('datetime-local value is local time without zone', () => {
  assert.equal(toDateTimeLocal(new Date(2026, 9, 31, 7, 5)), '2026-10-31T07:05')
})
