import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { test } from 'node:test'
import { setImmediate } from 'node:timers/promises'
import { transformWithOxc } from 'vite'

const source = new URL('../src/landing.ts', import.meta.url)
const { code } = await transformWithOxc(await readFile(source, 'utf8'), source.pathname)
const run = new (async () => {}).constructor('document', code)

test('the whole hero waits for both images to decode', async () => {
  const background = Promise.withResolvers()
  const screenshot = Promise.withResolvers()
  const classes = new Set()
  const hero = {
    querySelectorAll: () =>
      [background, screenshot].map(({ promise }) => ({ decode: () => promise })),
    classList: { add: (name) => classes.add(name) },
  }
  const loading = run({ querySelector: () => hero })
  background.resolve()
  await setImmediate()
  assert.equal(classes.has('is-ready'), false)
  screenshot.resolve()
  await loading
  assert.equal(classes.has('is-ready'), true)
})

test('an image failure still reveals the hero content', async () => {
  const classes = new Set()
  await run({
    querySelector: () => ({
      querySelectorAll: () => [{ decode: () => Promise.reject(new Error('Image unavailable')) }],
      classList: { add: (name) => classes.add(name) },
    }),
  })
  assert.equal(classes.has('is-ready'), true)
})
