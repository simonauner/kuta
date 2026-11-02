import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  loadChart,
  paceSeconds,
  parseThresholdPace,
  weeklyLoads,
  weekTrend,
} from '../src/lib/training-load.ts'

test('threshold pace converts between min/km text and m/s within the api limits', () => {
  assert.equal(parseThresholdPace('4:10'), 4)
  assert.equal(paceSeconds(4), 250)
  assert.equal(parseThresholdPace('1:40'), 10)
  assert.equal(parseThresholdPace('1:39'), null)
  assert.equal(parseThresholdPace('60:01'), null)
  assert.equal(parseThresholdPace('4'), null)
  assert.equal(parseThresholdPace('fyra'), null)
})

test('load chart shares one scale from zero for lines and bars', () => {
  const day = (load, fitness, fatigue) => ({ day: '2026-01-01', load, fitness, fatigue, form: 0 })
  const chart = loadChart([day(0, 10, 20), day(100, 50, 40)], {
    width: 220,
    height: 120,
    padding: 10,
  })
  assert.equal(chart.maximum, 100)
  assert.equal(chart.baseline, 110)
  assert.equal(chart.fitness, '60,100 160,60')
  assert.deepEqual(
    chart.bars.map((b) => [b.height, b.y]),
    [
      [0, 110],
      [100, 10],
    ],
  )
  assert.equal(loadChart([], { width: 1, height: 1, padding: 0 }), null)
})

test('weekly loads group local days into monday-first weeks ending with the current one', () => {
  const day = (day, load) => ({ day, load, fitness: 0, fatigue: 0, form: 0 })
  const days = [
    day('2026-10-25', 5),
    day('2026-10-26', 10),
    day('2026-11-01', 20),
    day('2026-11-02', 30),
  ]
  assert.deepEqual(weeklyLoads(days, 2), [
    { start: '2026-10-26', load: 30 },
    { start: '2026-11-02', load: 30 },
  ])
})

test('week trend compares the last full week with the three before and ignores the current week', () => {
  assert.equal(weekTrend([100, 100, 100, 131, 0]), 'more')
  assert.equal(weekTrend([100, 100, 100, 130, 999]), 'steady')
  assert.equal(weekTrend([100, 100, 100, 69, 0]), 'less')
  assert.equal(weekTrend([0, 0, 0, 10, 0]), 'more')
  assert.equal(weekTrend([0, 0, 0, 0, 50]), null)
  assert.equal(weekTrend([100, 100, 100, 100]), null)
})
