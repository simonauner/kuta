import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  formatGoalAmount,
  goalDaysLeft,
  goalFraction,
  goalInput,
  goalPeriodLabel,
  parseGoalInput,
} from '../src/lib/goals.ts'
import { isNamedSportGroup, sportGroupMembers } from '../src/lib/sport-groups.ts'

test('form values convert between km, minutes or whole sessions and SI targets', () => {
  assert.equal(parseGoalInput('distance', '10,5'), 10_500)
  assert.equal(parseGoalInput('duration', '30'), 1800)
  assert.equal(parseGoalInput('count', '2'), 2)
  for (const [metric, text] of [
    ['count', '1.5'],
    ['distance', '0'],
    ['distance', '-1'],
    ['duration', ''],
    ['distance', '1e3'],
  ])
    assert.equal(parseGoalInput(metric, text), null)
  assert.equal(goalInput('distance', 10_500), '10.5')
  assert.equal(goalInput('duration', 1800), '30')
  assert.equal(goalInput('count', 2), '2')
})

test('progress is clamped and amounts follow the metric', () => {
  assert.equal(goalFraction(5, 10), 0.5)
  assert.equal(goalFraction(25, 10), 1)
  assert.equal(goalFraction(0, 10), 0)
  assert.equal(formatGoalAmount('distance', 7_456, 'sv'), '7,46 km')
  assert.equal(formatGoalAmount('distance', 1_000_000, 'en'), '1,000 km')
  assert.equal(formatGoalAmount('duration', 1800), '30 min')
  assert.equal(formatGoalAmount('duration', 3600 * 2), '2 h')
  assert.equal(formatGoalAmount('duration', 3600 + 5 * 60 + 59), '1 h 5 min')
  assert.equal(formatGoalAmount('count', 3, 'sv'), '3')
})

test('period labels name ISO weeks, months and years; days left include today', () => {
  assert.deepEqual(goalPeriodLabel('week', '2026-12-28'), { week: 53, year: 2026 })
  assert.deepEqual(goalPeriodLabel('week', '2025-12-29'), { week: 1, year: 2026 })
  assert.deepEqual(goalPeriodLabel('year', '2026-01-01'), { year: 2026 })
  assert.deepEqual(goalPeriodLabel('month', '2026-10-01', 'en'), { month: 'October 2026' })
  assert.equal(goalDaysLeft('2026-11-02', new Date(2026, 9, 31, 23, 30)), 2)
  assert.equal(goalDaysLeft('2026-11-01', new Date(2026, 9, 31, 0, 5)), 1)
  assert.equal(goalDaysLeft('2026-10-01', new Date(2026, 9, 31)), 0)
})

test('groups with several sports have their own names; single sports use the sport name', () => {
  assert.equal(isNamedSportGroup('running'), true)
  assert.equal(isNamedSportGroup('cross-country skiing'), true)
  assert.equal(isNamedSportGroup('weight training'), false)
  assert.equal(isNamedSportGroup('toString'), false)
  const t = (key) => key
  assert.deepEqual(
    sportGroupMembers(
      { key: 'cross-country skiing', sports: ['nordic ski', 'crosscountryskiing'] },
      t,
    ),
    ['newActivity.sports.nordic ski'],
  )
})
