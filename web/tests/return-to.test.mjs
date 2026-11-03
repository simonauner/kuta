import assert from 'node:assert/strict'
import { test } from 'node:test'
import { rememberReturnTo, takeReturnTo } from '../src/lib/return-to.ts'

function memoryStorage() {
  const items = new Map()
  return {
    getItem: (k) => items.get(k) ?? null,
    setItem: (k, v) => items.set(k, v),
    removeItem: (k) => items.delete(k),
  }
}

test('a remembered link is returned once within a day', () => {
  const storage = memoryStorage()
  rememberReturnTo('/app/r/abc123', storage, 0)
  assert.equal(takeReturnTo(storage, 1000), '/app/r/abc123')
  assert.equal(takeReturnTo(storage, 1000), null)
})

test('old links and paths outside the app are ignored', () => {
  const storage = memoryStorage()
  rememberReturnTo('/app/r/abc123', storage, 0)
  assert.equal(takeReturnTo(storage, 24 * 60 * 60 * 1000), null)
  for (const path of ['https://evil.example/app/', '//evil.example', '/terms', '/app']) {
    rememberReturnTo(path, storage, 0)
    assert.equal(takeReturnTo(storage, 0), null, path)
  }
  storage.setItem('kuta.returnTo', 'not json')
  assert.equal(takeReturnTo(storage, 0), null)
})
