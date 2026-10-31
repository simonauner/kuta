import assert from 'node:assert/strict'
import { test } from 'node:test'
import { privacyCircle } from '../src/lib/privacy-circle.ts'

test('map circle is closed and uses meters at different latitudes', () => {
  for (const latitude of [0, 59, 89.99]) {
    const ring = privacyCircle(latitude, 18, 200)
    assert.equal(ring.length, 65)
    assert.deepEqual(ring[0], ring.at(-1))
    for (const [lon, lat] of ring) {
      const radians = Math.PI / 180
      const h =
        Math.sin(((lat - latitude) * radians) / 2) ** 2 +
        Math.cos(latitude * radians) *
          Math.cos(lat * radians) *
          Math.sin(((lon - 18) * radians) / 2) ** 2
      const distance = 6371000 * 2 * Math.asin(Math.sqrt(h))
      assert.ok(Math.abs(distance - 200) < 0.001)
    }
  }
})
