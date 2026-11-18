import assert from 'node:assert/strict'
import { test } from 'node:test'
import { isGymSport } from '../src/lib/sports.ts'

test('strength and mobility sports are gym sports, endurance and multisport are not', () => {
  assert.equal(isGymSport('workout'), true)
  assert.equal(isGymSport('weight training'), true)
  assert.equal(isGymSport('running'), false)
  assert.equal(isGymSport('triathlon'), false)
})
