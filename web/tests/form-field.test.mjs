import assert from 'node:assert/strict'
import { test } from 'node:test'
import { transformWithOxc } from 'vite'
import { readFile, writeFile, mkdtemp, rm } from 'node:fs/promises'
import { pathToFileURL } from 'node:url'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'

test('fields associate labels with unique controls, including inline, hidden and checkbox labels', async () => {
  const directory = await mkdtemp(new URL('../node_modules/form-field-test-', import.meta.url))
  try {
    for (const [name, path] of [
      ['utils', '../src/lib/utils.ts'],
      ['label', '../src/components/ui/label.tsx'],
      ['form-field', '../src/components/ui/form-field.tsx'],
    ]) {
      const source = new URL(path, import.meta.url)
      const content = (await readFile(source, 'utf8'))
        .replaceAll('@/lib/utils', './utils.mjs')
        .replaceAll('@/components/ui/label', './label.mjs')
      const { code } = await transformWithOxc(content, source.pathname, {
        jsx: { runtime: 'automatic' },
      })
      await writeFile(`${directory}/${name}.mjs`, code)
    }
    const { FormField } = await import(pathToFileURL(`${directory}/form-field.mjs`).href)
    const html = renderToStaticMarkup(
      createElement(
        'form',
        null,
        ...[
          { label: 'Title' },
          { label: 'Year', layout: 'inline' },
          { label: 'Language', hideLabel: true },
          { label: 'Consent', layout: 'choice' },
        ].map((props) =>
          createElement(FormField, { ...props, key: props.label }, (id) =>
            createElement('input', {
              id,
              type: props.layout === 'choice' ? 'checkbox' : 'text',
            }),
          ),
        ),
      ),
    )
    const ids = [...html.matchAll(/<input[^>]* id="([^"]+)"/g)].map((m) => m[1])
    const labels = [...html.matchAll(/<label[^>]* for="([^"]+)"/g)].map((m) => m[1])
    assert.equal(ids.length, 4)
    assert.equal(new Set(ids).size, 4)
    assert.deepEqual(labels.sort(), ids.sort())
    assert.match(
      html,
      /<label[^>]*><input[^>]*type="checkbox"[^>]*\/><span[^>]*>Consent<\/span><\/label>/,
    )
    assert.doesNotMatch(html, /<label[^>]*>(?:(?!<\/label>)[\s\S])*<label/)
  } finally {
    await rm(directory, { recursive: true, force: true })
  }
})
