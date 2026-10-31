import assert from 'node:assert/strict'
import { test } from 'node:test'
import { scheduleSlowLoading } from '../src/lib/slow-loading.ts'

test('fast loads never show a message, including after cancellation', (t) => {
  t.mock.timers.enable({ apis: ['setTimeout', 'Date'], now: 0 })
  const updates = []
  const cancel = scheduleSlowLoading(true, null, (value) => updates.push(value))
  t.mock.timers.tick(1999)
  assert.deepEqual(updates, [])
  cancel()
  t.mock.timers.tick(1000)
  assert.deepEqual(updates, [])
})

test('slow loads show after two seconds and stay for at least half a second', (t) => {
  t.mock.timers.enable({ apis: ['setTimeout', 'Date'], now: 0 })
  let shownAt = null
  scheduleSlowLoading(true, shownAt, (value) => {
    shownAt = value
  })
  t.mock.timers.tick(2000)
  assert.equal(shownAt, 2000)
  t.mock.timers.tick(100)
  scheduleSlowLoading(false, shownAt, (value) => {
    shownAt = value
  })
  t.mock.timers.tick(399)
  assert.equal(shownAt, 2000)
  t.mock.timers.tick(1)
  assert.equal(shownAt, null)
})

test('long loads need no extra delay; restarting cancels a pending hide', (t) => {
  t.mock.timers.enable({ apis: ['setTimeout', 'Date'], now: 2000 })
  let shownAt = 2000
  const update = (value) => {
    shownAt = value
  }
  const cancel = scheduleSlowLoading(false, shownAt, update)
  t.mock.timers.tick(100)
  cancel()
  assert.equal(scheduleSlowLoading(true, shownAt, update), undefined)
  t.mock.timers.tick(1000)
  assert.equal(shownAt, 2000)
  scheduleSlowLoading(false, shownAt, update)
  t.mock.timers.tick(0)
  assert.equal(shownAt, null)
})
