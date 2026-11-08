import assert from 'node:assert/strict'
import { test } from 'node:test'
import { distanceComparison, elevationComparison, yearChange } from '../src/lib/year-review.ts'

test('comparisons use the largest landmark the year reaches', () => {
  assert.equal(distanceComparison(30_000), null)
  assert.deepEqual(distanceComparison(84_390), { key: 'marathon', times: 2 })
  assert.equal(distanceComparison(940_000).key, 'stockholmGothenburg')
  assert.equal(distanceComparison(2_000_000).key, 'sweden')
  assert.equal(elevationComparison(200), null)
  assert.equal(elevationComparison(9_000).key, 'everest')
  assert.equal(elevationComparison(4_194).times, 2)
})

test('change against the previous year needs a previous year', () => {
  assert.equal(yearChange(1100, 1000), 10)
  assert.equal(yearChange(900, 1000), -10)
  assert.equal(yearChange(900, null), null)
  assert.equal(yearChange(900, 0), null)
})
