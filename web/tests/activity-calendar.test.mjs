import assert from 'node:assert/strict'
import { test } from 'node:test'
import { calendarWeeks, weekStreaks } from '../src/lib/activity-calendar.ts'

test('weeks start on Monday and end today', () => {
  // 2026-11-01 is a Sunday, 2026-11-04 a Wednesday.
  const weeks = calendarWeeks([], '2026-11-04', 2)
  assert.deepEqual(
    weeks.map((week) => week.map((d) => d.day)),
    [
      [
        '2026-10-26',
        '2026-10-27',
        '2026-10-28',
        '2026-10-29',
        '2026-10-30',
        '2026-10-31',
        '2026-11-01',
      ],
      ['2026-11-02', '2026-11-03', '2026-11-04'],
    ],
  )
})

test('levels follow the distance of the day and activities without distance still show', () => {
  const days = [
    { day: '2026-11-02', count: 1, distanceM: 0 },
    { day: '2026-11-03', count: 2, distanceM: 9_999 },
    { day: '2026-11-04', count: 1, distanceM: 20_000 },
  ]
  assert.deepEqual(
    calendarWeeks(days, '2026-11-05', 1)[0].map((d) => d.level),
    [1, 2, 4, 0],
  )
})

test('an empty current week does not break the streak, an empty past week does', () => {
  const week = (active) => [{ day: '', count: active ? 1 : 0, distanceM: 0, level: 0 }]
  assert.deepEqual(weekStreaks([true, true, false, true, true, true, false].map(week)), {
    current: 3,
    longest: 3,
  })
  assert.deepEqual(weekStreaks([true, true, true, false, true].map(week)), {
    current: 1,
    longest: 3,
  })
  assert.deepEqual(weekStreaks([true, false, false].map(week)), { current: 0, longest: 1 })
})
