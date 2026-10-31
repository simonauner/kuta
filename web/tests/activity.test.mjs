import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  activitySearch,
  activityTitle,
  chartAxis,
  formatDuration,
  formatPace,
  groupActivities,
  parseDuration,
  splits,
  toDateTimeLocal,
} from '../src/lib/activity.ts'

// 2,502 km på 834 s: 3 m/s, en mätpunkt per sekund, höjden stiger 1 m per 100 m, puls 150.
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
    result.map((s) => s.distanceM),
    [1000, 1000, 502],
  )
  for (const split of result) assert.ok(Math.abs(split.durationS - split.distanceM / 3) < 1e-9)
  assert.equal(result[0].elevationM, 10)
  assert.equal(result[0].avgHr, 150)
})

test('a few metres after the last kilometre are noise, not a split', () => {
  assert.deepEqual(
    splits(run({ seconds: 340 })).map((s) => s.km),
    [1],
  )
})

test('missing distance points are skipped, and no distance means no splits', () => {
  assert.ok(Math.abs(splits(run({ gapAt: 334 }))[0].durationS - 1000 / 3) < 1e-9)
  assert.deepEqual(splits({ ...run(), dist: null }), [])
})

test('sparse points interpolate every crossed kilometre and its measurements', () => {
  const result = splits({
    ...run(),
    t: [0, 900, 1200],
    dist: [0, 3000, 4000],
    alt: [10, 40, 30],
    hr: [140, 160, 150],
  })
  assert.deepEqual(
    result.map((s) => [s.km, s.distanceM, s.durationS, s.elevationM, s.avgHr]),
    [
      [1, 1000, 300, 10, 143],
      [2, 1000, 300, 10, 150],
      [3, 1000, 300, 10, 157],
      [4, 1000, 300, -10, 155],
    ],
  )
})

test('splits use measured endpoints and reject distance or time resets', () => {
  const stream = {
    ...run(),
    t: [0, 60, 360, 660, 700],
    dist: [null, 500, 1500, 2500, null],
    alt: null,
    hr: null,
  }
  assert.deepEqual(
    splits(stream).map((s) => [s.distanceM, s.durationS]),
    [
      [500, 150],
      [1000, 300],
      [500, 150],
    ],
  )
  assert.deepEqual(splits({ ...stream, dist: [0, 1000, 0, 1000, 2000] }), [])
  assert.deepEqual(splits({ ...stream, t: [0, 60, 30, 90, 120] }), [])
})

test('charts use elapsed time without usable distance and kilometres otherwise', () => {
  const stream = { ...run(), t: [0, 300, 600, 900], dist: [0, 1000, null, 2000] }
  assert.deepEqual(chartAxis(stream), { values: [0, 1, 1, 2], elapsedTime: false })
  for (const dist of [null, [0, 0, null, 0], [null, 1000, 1000, 1000], [0, 1000, 0, 2000]])
    assert.deepEqual(chartAxis({ ...stream, dist }), { values: stream.t, elapsedTime: true })
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

const activityAt = (date, showMonth = true, showWeek = true) => ({
  id: date,
  startedAt: `${date}T12:00:00`,
  showMonth,
  showWeek,
})
const periods = (activities) =>
  groupActivities(activities).map((period) => ({
    year: period.year,
    month: period.month?.getMonth() ?? null,
    week: period.week,
    dates: period.activities.map((a) => a.id),
  }))

test('period headings follow metadata for the complete history, even with one loaded activity', () => {
  assert.deepEqual(periods([]), [])
  assert.deepEqual(periods([activityAt('2026-10-30', false, false)]), [
    { year: 2026, month: null, week: null, dates: ['2026-10-30'] },
  ])
  assert.deepEqual(periods([activityAt('2026-10-30')]), [
    { year: 2026, month: 9, week: 44, dates: ['2026-10-30'] },
  ])
  assert.deepEqual(periods([activityAt('2026-10-30', true, false)]), [
    { year: 2026, month: 9, week: null, dates: ['2026-10-30'] },
  ])
})

test('appended pages merge within a week and add only new period headings', () => {
  const first = activityAt('2026-10-30')
  const second = activityAt('2026-10-29')
  const result = periods([
    first,
    second,
    activityAt('2026-09-30', true, false),
    activityAt('2025-12-30', false, false),
  ])
  assert.deepEqual(result, [
    { year: 2026, month: 9, week: 44, dates: ['2026-10-30', '2026-10-29'] },
    { year: null, month: 8, week: null, dates: ['2026-09-30'] },
    { year: 2025, month: null, week: null, dates: ['2025-12-30'] },
  ])
  assert.deepEqual({ ...result[0], dates: [first.id] }, periods([first])[0])
})

test('ISO weeks start on Monday and may belong to another year', () => {
  assert.deepEqual(
    periods(
      ['2021-01-03', '2021-01-04', '2020-12-31', '2020-12-28'].map((date) => activityAt(date)),
    ),
    [
      { year: 2021, month: 0, week: 1, dates: ['2021-01-04'] },
      { year: null, month: null, week: 53, dates: ['2021-01-03'] },
      { year: 2020, month: 11, week: 53, dates: ['2020-12-31', '2020-12-28'] },
    ],
  )
})

test('month boundaries stay separate even within the same ISO week', () => {
  assert.deepEqual(
    periods(
      ['2026-10-01', '2026-09-30', '2026-09-29', '2026-10-02'].map((date) => activityAt(date)),
    ),
    [
      { year: 2026, month: 9, week: 40, dates: ['2026-10-02', '2026-10-01'] },
      { year: null, month: 8, week: 40, dates: ['2026-09-30', '2026-09-29'] },
    ],
  )
})

test('grouping follows the displayed local date and does not mutate the input', () => {
  const local = new Date(2026, 0, 1, 0, 15)
  const activities = [
    activityAt('2025-10-30'),
    { ...activityAt('2026-01-01'), startedAt: local.toISOString() },
  ]
  const original = [...activities]
  const grouped = groupActivities(activities)
  assert.equal(grouped[0].year, 2026)
  assert.deepEqual(activities, original)
})

test('activity search keeps a valid year and defaults to all years for invalid values', () => {
  assert.deepEqual(activitySearch({ year: '2024' }), { year: 2024 })
  for (const year of [1, 9998]) assert.deepEqual(activitySearch({ year }), { year })
  for (const year of [undefined, '', null, true, [2024], {}, 'older', 0, 9999, 2024.5])
    assert.deepEqual(activitySearch({ year }), {})
})

test('activity search preserves supported tags alongside the year', () => {
  for (const tag of ['race', 'workout', 'long_run', 'easy'])
    assert.deepEqual(activitySearch({ year: '2026', tag }), { year: 2026, tag })
  for (const tag of ['', 'unknown', '__proto__', 'toString', ['race'], true, null])
    assert.deepEqual(activitySearch({ tag }), {})
})
