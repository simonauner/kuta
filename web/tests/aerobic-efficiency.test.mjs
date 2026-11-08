import assert from 'node:assert/strict'
import { test } from 'node:test'
import { aerobicChange, aerobicTrend } from '../src/lib/aerobic-efficiency.ts'

const point = (day, speedMS, hr) => ({ activityId: day, day, speedMS, hr })

test('speed is scaled to the usual heart rate and summarized per month', () => {
  const trend = aerobicTrend([
    point('2026-03-02', 2.8, 140),
    point('2026-03-20', 3.0, 150),
    point('2026-04-10', 3.15, 145),
    point('2026-05-01', 3.3, 145),
  ])
  assert.equal(trend.referenceHr, 145)
  assert.deepEqual(
    trend.months.map((m) => [m.month, m.count]),
    [
      ['2026-03', 2],
      ['2026-04', 1],
      ['2026-05', 1],
    ],
  )
  // 2.8 × 145/140 = 2.9 and 3.0 × 145/150 = 2.9
  assert.ok(Math.abs(trend.months[0].speedMS - 2.9) < 1e-9)
  const change = aerobicChange(trend)
  assert.ok(change.to < change.from)
})

test('runs far from the usual heart rate and short histories give no trend', () => {
  const trend = aerobicTrend([
    point('2026-03-02', 3, 145),
    point('2026-03-09', 3, 145),
    point('2026-04-02', 3, 145),
    // A race at 175: outside the band
    point('2026-04-10', 4.5, 175),
  ])
  assert.equal(
    trend.months.reduce((sum, m) => sum + m.count, 0),
    3,
  )
  assert.equal(aerobicTrend([point('2026-03-02', 3, 145), point('2026-03-09', 3, 145)]), null)
  assert.equal(
    aerobicChange({ referenceHr: 145, months: [{ month: '2026-03', speedMS: 3, count: 3 }] }),
    null,
  )
})
