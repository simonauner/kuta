import assert from 'node:assert/strict'
import { test } from 'node:test'
import { currentLocation, LocationError } from '../src/lib/geolocation.ts'

test('requests one fresh accurate position and returns only the map point', async () => {
  let calls = 0
  const result = await currentLocation({
    getCurrentPosition(success, failure, options) {
      calls++
      assert.deepEqual(options, { enableHighAccuracy: true, timeout: 10000, maximumAge: 0 })
      success({ coords: { latitude: 59.33, longitude: 18.06, accuracy: 35, altitude: 123 } })
    },
  })
  assert.equal(calls, 1)
  assert.deepEqual(result, { latitude: 59.33, longitude: 18.06 })
})

test('permission denial, timeout and unavailable position have stable errors without browser messages', async () => {
  for (const [code, expected] of [
    [1, 'locationDenied'],
    [2, 'locationUnavailable'],
    [3, 'locationTimeout'],
  ]) {
    await assert.rejects(
      currentLocation({
        getCurrentPosition(success, failure) {
          failure({ code, message: 'Synthetic private provider message' })
        },
      }),
      (error) =>
        error instanceof LocationError &&
        error.code === expected &&
        !error.message.includes('Synthetic'),
    )
  }
})

test('unsupported geolocation, browser exceptions and invalid positions fail without selecting a point', async () => {
  await assert.rejects(currentLocation(undefined), { code: 'locationUnsupported' })
  await assert.rejects(
    currentLocation({
      getCurrentPosition() {
        throw new Error('Synthetic browser error')
      },
    }),
    { code: 'locationUnavailable' },
  )
  await assert.rejects(
    currentLocation({
      getCurrentPosition(success) {
        success({ coords: { latitude: NaN, longitude: 18, accuracy: 10 } })
      },
    }),
    { code: 'locationUnavailable' },
  )
})
