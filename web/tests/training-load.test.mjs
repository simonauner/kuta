import assert from 'node:assert/strict'
import { test } from 'node:test'
import { loadChart, paceSeconds, parseThresholdPace } from '../src/lib/training-load.ts'

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
