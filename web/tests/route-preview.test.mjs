import assert from 'node:assert/strict'
import { test } from 'node:test'
import { routePreviewLines } from '../src/lib/route-preview.ts'

test('map previews retain separate clipped sections and omit empty geometry', () => {
  const first = [
    [18, 59],
    [18.01, 59.01],
  ]
  const second = [
    [18.03, 59.02],
    [18.04, 59.03],
  ]
  assert.deepEqual(routePreviewLines(JSON.stringify({ type: 'LineString', coordinates: first })), [
    first,
  ])
  assert.deepEqual(
    routePreviewLines(JSON.stringify({ type: 'MultiLineString', coordinates: [first, second] })),
    [first, second],
  )
  assert.deepEqual(routePreviewLines('{"type":"LineString","coordinates":[]}'), [])
})
