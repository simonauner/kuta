import assert from 'node:assert/strict'
import { test } from 'node:test'
import { inviteLink, shiftWeek } from '../src/lib/groups.ts'

test('weeks shift by seven days across month, year and daylight saving changes', () => {
  assert.equal(shiftWeek('2026-03-23', 1), '2026-03-30')
  assert.equal(shiftWeek('2026-03-30', -1), '2026-03-23')
  assert.equal(shiftWeek('2026-12-28', 1), '2027-01-04')
})

test('invite links point at the app join page on the same origin', () => {
  assert.equal(inviteLink('k7m2qx9p', 'https://kutaapp.com'), 'https://kutaapp.com/app/g/k7m2qx9p')
})
