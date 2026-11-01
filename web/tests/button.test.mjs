import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { test } from 'node:test'

const src = new URL('../src', import.meta.url).pathname
const files = (dir) =>
  readdirSync(dir, { withFileTypes: true }).flatMap((entry) =>
    entry.isDirectory()
      ? files(join(dir, entry.name))
      : entry.name.endsWith('.tsx')
        ? [join(dir, entry.name)]
        : [],
  )
// Placement only: appearance belongs to the Button variants.
const layout =
  /^((sm|md|lg):)?(-?m[trblxy]?-|w-|min-w-|max-w-|self-|col-span-|flex-1|shrink|grow|order-|hidden|block|inline-flex)/

test('buttons get their appearance from variants, with className only for layout', () => {
  const offenders = []
  for (const file of files(src)) {
    if (file.endsWith('ui/button.tsx')) continue
    for (const [, attributes] of readFileSync(file, 'utf8').matchAll(/<Button\b([^<]*?)>/gs)) {
      const className = attributes.match(/className="([^"]*)"/)?.[1]
      for (const name of className?.split(/\s+/).filter(Boolean) ?? [])
        if (!layout.test(name)) offenders.push(`${file.slice(src.length)}: ${name}`)
    }
  }
  assert.deepEqual(offenders, [])
})
