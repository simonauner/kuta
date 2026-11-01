import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  dayOfYear,
  relativeChange,
  spreadLabels,
  yearComparison,
} from '../src/lib/year-comparison.ts'

const total = (day, distanceM) => ({ day, count: 1, distanceM, durationS: distanceM / 3 })

test('years accumulate newest first and the current year stops today', () => {
  const series = yearComparison(
    [total('2025-03-01', 10000), total('2026-01-02', 5000), total('2025-12-31', 2000)],
    'distanceM',
    { year: 2026, month: 1, day: 10 },
  )
  assert.deepEqual(
    series.map((s) => s.year),
    [2026, 2025],
  )
  assert.deepEqual(series[0].points, [
    { day: 0, value: 0 },
    { day: 2, value: 5000 },
    { day: 10, value: 5000 },
  ])
  assert.deepEqual(series[1].points.at(-1), { day: 365, value: 12000 })
  assert.equal(series[1].total, 12000)
  assert.equal(series[1].toDate, 0)
})

test('same date compares month and day, so leap days do not shift the cutoff', () => {
  // 1 March is day 61 in leap years and day 60 otherwise; the comparison must still include the 1 March activity.
  const [leap, normal] = yearComparison(
    [total('2024-03-01', 1000), total('2024-03-02', 1000), total('2023-03-01', 4000)],
    'distanceM',
    { year: 2026, month: 3, day: 1 },
  )
  assert.equal(dayOfYear({ year: 2024, month: 3, day: 1 }), 61)
  assert.equal(leap.toDate, 1000)
  assert.equal(leap.points.at(-1).day, 366)
  assert.equal(normal.toDate, 4000)
})

test('the selected metric is accumulated and change needs a positive comparison value', () => {
  const [year] = yearComparison([total('2026-01-01', 3000)], 'count', {
    year: 2026,
    month: 6,
    day: 1,
  })
  assert.equal(year.total, 1)
  assert.equal(relativeChange(150, 100), 0.5)
  assert.equal(relativeChange(5, 0), null)
  assert.equal(relativeChange(5, undefined), null)
})

test('year labels that end at the same height are spread apart within the chart', () => {
  assert.deepEqual(spreadLabels([50, 50, 10], 14, 0, 100), [50, 64, 10])
  assert.deepEqual(spreadLabels([98, 99], 14, 0, 100), [86, 100])
})
