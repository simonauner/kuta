import assert from 'node:assert/strict'
import { test } from 'node:test'
import { recordMeters, trendChart } from '../src/lib/records.ts'

const effort = (achievedAt, elapsedS) => ({
  activityId: achievedAt,
  distanceM: 5000,
  elapsedS,
  startOffsetS: 0,
  achievedAt,
})
const size = { width: 100, height: 50, padding: 10 }

test('record labels map to the exact distances used by the record engine', () => {
  assert.equal(recordMeters(1609), 1609.344)
  assert.equal(recordMeters(21097), 21097.5)
  assert.equal(recordMeters(5000), 5000)
})

test('trend chart puts faster times higher and steps each record until the next', () => {
  const january = effort('2026-01-01T00:00:00Z', 1500)
  const middle = effort('2026-01-11T00:00:00Z', 1400)
  const last = effort('2026-01-21T00:00:00Z', 1450)
  const chart = trendChart(
    {
      months: [january, middle, last].map((e) => ({ ...e, year: 2026, month: 1 })),
      progression: [january, middle],
    },
    size,
  )
  assert.deepEqual(
    chart.months.map((p) => [Math.round(p.x), Math.round(p.y)]),
    [
      [10, 40],
      [50, 10],
      [90, 25],
    ],
  )
  // The latest record holds until the last effort on the chart.
  assert.equal(chart.steps, '10,40 50,40 50,10 90,10')
  assert.equal(trendChart({ months: [], progression: [] }, size), null)
})

test('a single effort is centred instead of dividing by zero', () => {
  const only = effort('2026-01-01T00:00:00Z', 1500)
  const chart = trendChart(
    { months: [{ ...only, year: 2026, month: 1 }], progression: [only] },
    size,
  )
  assert.deepEqual([chart.records[0].x, chart.records[0].y], [50, 25])
  assert.equal(chart.steps, '50,25 50,25')
})
