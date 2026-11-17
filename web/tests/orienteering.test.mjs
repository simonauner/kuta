import assert from 'node:assert/strict'
import { test } from 'node:test'
import { courseLengthM, courseStats, orienteeringLegs } from '../src/lib/orienteering.ts'

// Northwards, one sample per second; northM gives the position at each second.
function streams(northM) {
  const t = northM.map((_, i) => i)
  return { t, lat: northM.map((m) => 59 + m / 111_320), lon: t.map(() => 18) }
}

const lap = (startS, durationS, distanceM, movingS = null) => ({
  startS,
  durationS,
  distanceM,
  movingS,
})

test('a leg slow per straight-line km compared with the others is a probable miss', () => {
  // Four 200 m legs straight north at 4 m/s, except the third, which goes 100 m on in 150 s.
  const north = []
  for (let s = 0; s <= 50; s++) north.push(4 * s)
  for (let s = 1; s <= 50; s++) north.push(200 + 4 * s)
  for (let s = 1; s <= 150; s++) north.push(400 + (100 * s) / 150)
  for (let s = 1; s <= 50; s++) north.push(500 + 4 * s)
  const legs = orienteeringLegs(
    [lap(0, 50, 200, 50), lap(50, 50, 200), lap(100, 150, 600, 90), lap(250, 50, 200)],
    streams(north),
  )
  assert.deepEqual(
    legs.map((l) => l.miss),
    [false, false, true, false],
  )
  assert.equal(Math.round(legs[2].straightM), 100)
  assert.equal(legs[2].stillS, 60)
  assert.equal(legs[1].stillS, null)
})

test('without GPS or with too few legs to compare, nothing is judged', () => {
  const laps = [lap(0, 50, 200), lap(50, 500, 200), lap(550, 50, 200)]
  assert.ok(orienteeringLegs(laps, null).every((l) => !l.miss && l.straightM === null))
  const north = Array.from({ length: 101 }, (_, s) => 4 * s)
  assert.ok(orienteeringLegs(laps.slice(0, 2), streams(north)).every((l) => !l.miss))
})

test('km time is counted against the course length', () => {
  const { kmTimeS, overdistance } = courseStats(3000, 6000, 5000)
  assert.equal(kmTimeS, 600)
  assert.ok(Math.abs(overdistance - 0.2) < 1e-9)
})

test('only orienteering keeps a course length', () => {
  assert.equal(courseLengthM('orienteering', '2.5'), 2500)
  assert.equal(courseLengthM('orienteering', ''), null)
  assert.equal(courseLengthM('running', '2.5'), null)
})
