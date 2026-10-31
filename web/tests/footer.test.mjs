import assert from 'node:assert/strict'
import { test } from 'node:test'
import { transformWithOxc } from 'vite'
import { readFile, writeFile, mkdtemp, rm } from 'node:fs/promises'
import { pathToFileURL } from 'node:url'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import sv from '../src/locales/sv.json' with { type: 'json' }
import en from '../src/locales/en.json' with { type: 'json' }

test('shared footer renders the logo and switches public navigation for the translated app version', async () => {
  const directory = await mkdtemp(new URL('../node_modules/footer-test-', import.meta.url))
  const source = new URL('../src/components/footer.tsx', import.meta.url)
  const { code } = await transformWithOxc(await readFile(source, 'utf8'), source.pathname, {
    jsx: { runtime: 'automatic' },
  })
  const compiled = `${directory}/footer.mjs`
  await writeFile(compiled, code)
  try {
    const { Footer } = await import(pathToFileURL(compiled).href)
    const publicHtml = renderToStaticMarkup(
      createElement(Footer, { variant: 'public', labels: sv.footer }),
    )
    assert.match(publicHtml, /alt="Kuta"/)
    assert.match(publicHtml, /href="\/app">Öppna appen/)
    assert.match(publicHtml, /href="\/privacy">Integritetspolicy/)
    assert.doesNotMatch(publicHtml, /Version/)
    const appHtml = renderToStaticMarkup(
      createElement(Footer, {
        variant: 'app',
        language: 'en',
        labels: en.footer,
        version: 'Version <test>',
      }),
    )
    assert.match(appHtml, /alt="Kuta"/)
    assert.match(appHtml, /href="\/privacy#english">Privacy policy/)
    assert.match(appHtml, /Version &lt;test&gt;/)
    assert.doesNotMatch(appHtml, /Open app/)
  } finally {
    await rm(directory, { recursive: true, force: true })
  }
})
