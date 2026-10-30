import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { test } from 'node:test'
import { detectLanguage } from '../src/lib/language.ts'

const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8')
const en = JSON.parse(read('../src/locales/en.json'))
const sv = JSON.parse(read('../src/locales/sv.json'))

test('saved choice wins; browser regions resolve to supported languages; unsupported languages fall back', () => {
  assert.equal(detectLanguage('sv', ['en-US']), 'sv')
  assert.equal(detectLanguage('en', ['sv-SE']), 'en')
  assert.equal(detectLanguage(null, ['fr-FR', 'sv-SE', 'en']), 'sv')
  assert.equal(detectLanguage('invalid', ['EN-GB', 'sv']), 'en')
  assert.equal(detectLanguage(null, ['de-DE']), 'en')
  assert.equal(detectLanguage(null, []), 'en')
})

test('Swedish IP wins over browser language but never an explicit choice', () => {
  assert.equal(detectLanguage(null, ['en-US'], true), 'sv')
  assert.equal(detectLanguage(null, ['de-DE'], true), 'sv')
  assert.equal(detectLanguage('en', ['sv-SE'], true), 'en')
  assert.equal(detectLanguage('sv', ['en-US'], false), 'sv')
  assert.equal(detectLanguage('invalid', ['en-US'], true), 'sv')
  assert.equal(detectLanguage(null, ['en-US'], false), 'en')
})

test('Swedish and English have the same keys and non-empty text', () => {
  function keys(object, prefix = '') {
    return Object.entries(object)
      .flatMap(([key, value]) => {
        if (typeof value === 'object') return keys(value, `${prefix}${key}.`)
        assert.equal(typeof value, 'string')
        assert.ok(value.trim())
        return `${prefix}${key}`
      })
      .sort()
  }
  assert.deepEqual(keys(sv), keys(en))
})

test('every API error has a stable code translated in both languages', () => {
  const apiDir = new URL('../../api/Kuta.Api/', import.meta.url)
  for (const file of readdirSync(apiDir).filter((name) => name.endsWith('.cs'))) {
    const source = readFileSync(new URL(file, apiDir), 'utf8')
    for (const match of source.matchAll(/new ApiError\("([^"]*)"(?:, "([^"]*)")?\)/g)) {
      const [, message, code] = match
      assert.ok(code, `${file}: missing error code for ${message}`)
      assert.equal(sv.errors[code], message, `${file}: Swedish error text for ${code}`)
      assert.ok(en.errors[code], `${file}: English error text for ${code}`)
    }
  }
})
