import assert from 'node:assert/strict'
import { test } from 'node:test'
import { axisTicks, change, lineRuns, statisticsRanges } from '../src/lib/statistics-period.ts'

// Sunday 8 November 2026.
const today = { year: 2026, month: 11, day: 8 }

test('rolling weeks start on a Monday and include the current week', () => {
  assert.deepEqual(statisticsRanges('12w', today), {
    current: { from: '2026-08-17', to: '2026-11-09', bucket: 'week' },
    previous: { from: '2026-05-25', to: '2026-08-17', bucket: 'week' },
  })
  // On a Monday the current week is a single day, and the previous period is equally long.
  assert.deepEqual(statisticsRanges('4w', { year: 2026, month: 1, day: 5 }), {
    current: { from: '2025-12-15', to: '2026-01-06', bucket: 'week' },
    previous: { from: '2025-11-23', to: '2025-12-15', bucket: 'week' },
  })
})

test('months and years compare with the same span a year earlier', () => {
  assert.deepEqual(statisticsRanges('12m', today), {
    current: { from: '2025-12-01', to: '2026-11-09', bucket: 'month' },
    previous: { from: '2024-12-01', to: '2025-11-09', bucket: 'month' },
  })
  assert.deepEqual(statisticsRanges(2026, today).previous, {
    from: '2025-01-01',
    to: '2025-11-09',
    bucket: 'month',
  })
  assert.deepEqual(statisticsRanges(2024, today), {
    current: { from: '2024-01-01', to: '2025-01-01', bucket: 'month' },
    previous: { from: '2023-01-01', to: '2024-01-01', bucket: 'month' },
  })
})

test('change needs something to compare with', () => {
  assert.equal(change(197.8, 78.1).toFixed(0), '153')
  assert.equal(change(5, 0), null)
})

test('lines break at missing values', () => {
  assert.deepEqual(lineRuns([1, 0, null, 3, null, null, 4, 5]), [[0, 1], [3], [6, 7]])
  assert.deepEqual(lineRuns([null]), [])
})

test('gridlines use round steps with little headroom, from zero only for sums', () => {
  assert.deepEqual(axisTicks('distanceM', [24600, 44800, 0]), [0, 25000, 50000])
  assert.deepEqual(axisTicks('count', [3, 7]), [0, 2, 4, 6, 8])
  // Averages start below the lowest value; missing values are ignored.
  assert.deepEqual(axisTicks('paceSPerKm', [293, 226, null, 159]), [120, 180, 240, 300])
  assert.deepEqual(axisTicks('avgHr', [157.1, 143.1]), [140, 145, 150, 155, 160])
  // Even a flat series gets two intervals.
  assert.deepEqual(axisTicks('avgHr', [150, 150]), [150, 151, 152])
})
