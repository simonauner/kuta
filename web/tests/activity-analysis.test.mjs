import assert from 'node:assert/strict'
import { test } from 'node:test'
import { gradeFactor, heartRateDrift, intervals, pacing } from '../src/lib/activity-analysis.ts'

// One sample per second; speed(s) and hr(s) give the value at each second.
function run(seconds, speed, hr = () => 150, alt = null) {
  const t = []
  const dist = []
  const hrs = []
  let d = 0
  for (let s = 0; s <= seconds; s++) {
    t.push(s)
    dist.push(d)
    hrs.push(hr(s))
    d += speed(s)
  }
  return {
    t,
    dist,
    hr: hrs,
    speed: null,
    alt: alt ? t.map(alt) : null,
    lat: null,
    lon: null,
    cadence: null,
    temp: null,
  }
}

test('grade factor matches the API on flat ground and makes climbing cost more', () => {
  assert.equal(gradeFactor(0), 1)
  assert.ok(gradeFactor(0.1) > 1.5)
  assert.ok(gradeFactor(-0.05) < 1)
  assert.equal(gradeFactor(1), gradeFactor(0.45))
})

test('steady heart rate gives no drift and a rising one is reported in steps', () => {
  const steady = heartRateDrift(run(3600, () => 3))
  assert.equal(steady.level, 'stable')
  assert.ok(Math.abs(steady.percent) < 0.5)

  // Same pace, heart rate rising from 140 to 160 after the warm-up
  const rising = heartRateDrift(
    run(
      3600,
      () => 3,
      (s) => 140 + (20 * s) / 3600,
    ),
  )
  assert.ok(rising.percent > 5 && rising.percent < 10, `${rising.percent}`)
  assert.equal(rising.level, 'drifted')

  const high = heartRateDrift(
    run(
      3600,
      () => 3,
      (s) => 130 + (40 * s) / 3600,
    ),
  )
  assert.equal(high.level, 'high')
})

test('drift ignores the warm-up and needs 20 steady minutes with heart rate', () => {
  // A low heart rate during the first ten minutes doesn't count
  const warmUp = heartRateDrift(
    run(
      3600,
      () => 3,
      (s) => (s < 600 ? 100 : 150),
    ),
  )
  assert.equal(warmUp.level, 'stable')
  assert.equal(heartRateDrift(run(29 * 60, () => 3)), null)
  assert.equal(heartRateDrift({ ...run(3600, () => 3), hr: null }), null)
  // Intervals: 2 min fast, 2 min slow
  assert.equal(heartRateDrift(run(3600, (s) => (Math.floor(s / 120) % 2 ? 2 : 4.5))), null)
})

test('drift uses grade-adjusted speed, so a climb in the second half is not drift', () => {
  // Slower uphill in the second half with the same heart rate: the same effort
  const climb = run(
    3600,
    (s) => (s < 2100 ? 3 : 2.4),
    () => 150,
    (s) => (s < 2100 ? 0 : (s - 2100) * 2.4 * 0.05),
  )
  const drift = heartRateDrift(climb)
  assert.ok(drift.percent < 5, `${drift.percent}`)
})

test('pacing reports split and finish on the moving clock', () => {
  const even = pacing(run(2000, () => 3))
  assert.equal(even.split, 'even')
  assert.ok(Math.abs(even.finishPercent) < 0.5)

  const negative = pacing(run(2000, (s) => (s < 1000 ? 3 : 3.3)))
  assert.equal(negative.split, 'negative')
  assert.ok(negative.percent < -2)

  // Fading at the end: the last fifth is slower than the rest
  const fade = pacing(run(2000, (s) => (s < 1700 ? 3.3 : 2.5)))
  assert.equal(fade.split, 'positive')
  assert.ok(fade.finishPercent > 10)
})

test('a pause in the stream does not count as slow running', () => {
  const streams = run(2000, () => 3)
  // Auto-pause: ten minutes without samples in the second half
  const paused = { ...streams, t: streams.t.map((s) => (s > 1500 ? s + 600 : s)) }
  assert.equal(pacing(paused).split, 'even')
  assert.equal(pacing(run(900, () => 3)), null)
})

const lap = (distanceM, durationS, avgHr = null) => ({
  lapIndex: 0,
  startS: 0,
  durationS,
  movingS: durationS,
  distanceM,
  avgHr,
  maxHr: null,
  ascentM: null,
  descentM: null,
  avgCadence: null,
})

test('intervals are found among the laps with warm-up, recoveries and cool-down', () => {
  const laps = [lap(2000, 720)]
  for (let i = 0; i < 4; i++) laps.push(lap(1000, 225 + i, 170), lap(300, 120, 130))
  laps.push(lap(2000, 720))
  const result = intervals(laps)
  assert.equal(result.reps.length, 4)
  assert.deepEqual(
    result.reps.map((r) => r.recoveryS),
    [120, 120, 120, null],
  )
  assert.equal(result.reps[0].avgHr, 170)
  assert.equal(result.fastestPace, 225)
  assert.equal(result.slowestPace, 228)
  assert.ok(result.lastPercent > 0)
})

test('autolap inside a rep becomes one rep, and a steady run is not intervals', () => {
  const tempo = intervals([
    lap(1000, 360),
    lap(1000, 250),
    lap(1000, 250),
    lap(1000, 360),
    lap(1000, 250),
    lap(1000, 250),
    lap(1000, 360),
  ])
  assert.deepEqual(
    tempo.reps.map((r) => r.distanceM),
    [2000, 2000],
  )
  assert.equal(intervals([lap(1000, 300), lap(1000, 310), lap(1000, 295), lap(1000, 305)]), null)
  // An accidental short lap doesn't count as a rep
  assert.equal(intervals([lap(3000, 900), lap(20, 3), lap(3000, 900), lap(20, 3)]), null)
})
