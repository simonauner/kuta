import assert from 'node:assert/strict'
import { test } from 'node:test'
import { durationTicks } from '../src/lib/duration-ticks.ts'

test('ticks are round times that cover the range with at most five lines', () => {
  assert.deepEqual(durationTicks(870, 930), { lo: 870, hi: 930, ticks: [870, 900, 930] })
  assert.deepEqual(durationTicks(875, 965).ticks, [870, 900, 930, 960, 990])
  assert.deepEqual(durationTicks(1500, 1600).ticks, [1500, 1560, 1620])
  assert.ok(durationTicks(3000, 9000).ticks.length <= 5)
})

test('equal times still get an axis with two lines', () => {
  assert.deepEqual(durationTicks(900, 900), { lo: 900, hi: 905, ticks: [900, 905] })
})
