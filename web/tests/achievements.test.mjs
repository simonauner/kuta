import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { achievementText } from '../src/lib/achievements.ts'

const sv = JSON.parse(readFileSync(new URL('../src/locales/sv.json', import.meta.url), 'utf8'))
// Resolves a key in the Swedish texts and fills in the values like i18next
const t = (key, values = {}) =>
  key
    .split('.')
    .reduce((node, part) => node[part], sv)
    .replace(/\{\{(\w+)\}\}/g, (_, name) => String(values[name]))

const achievement = (kind, threshold, year = null) => ({
  key: kind,
  kind,
  threshold,
  year,
  achievedAt: '2026-01-01T00:00:00Z',
  activityId: 'a',
  seen: false,
})

test('thresholds with a comparison use it and others the text for the kind', () => {
  assert.equal(
    achievementText(achievement('climb', 8849, 2026), t, 'sv').detail,
    'Lika högt som Mount Everest, 8 849 m, sedan nyår.',
  )
  assert.equal(
    achievementText(achievement('runDistance', 5_000_000), t, 'sv').title,
    '5 000 km löpning',
  )
  assert.equal(
    achievementText(achievement('runDistance', 5_000_000), t, 'sv').detail,
    sv.achievements.kinds.runDistance.detail,
  )
  assert.equal(
    achievementText(achievement('firstDistance', 21097), t, 'sv').title,
    'Första halvmaran!',
  )
})
