import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createServer } from 'vite'
import { createElement } from 'react'
import { renderToString } from 'react-dom/server'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import {
  createRootRoute,
  createRouter,
  createMemoryHistory,
  RouterProvider,
} from '@tanstack/react-router'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import sv from '../src/locales/sv.json' with { type: 'json' }

// Render the real feed with cached responses; no HTTP or database is needed for presentation rules.
test('only an empty home feed offers onboarding, with different copy when following runners', async () => {
  const server = await createServer({
    configFile: false,
    root: new URL('..', import.meta.url).pathname,
    resolve: { alias: { '@': new URL('../src', import.meta.url).pathname } },
    server: { middlewareMode: true, watch: null, ws: false },
    appType: 'custom',
  })
  const previousWindow = globalThis.window
  globalThis.window = { location: { origin: 'http://localhost' } }
  try {
    const { ActivityFeed } = await server.ssrLoadModule('/src/components/activity-feed.tsx')
    const i18n = createInstance()
    await i18n.init({ lng: 'sv', resources: { sv: { translation: sv } } })
    const render = async ({ following = [], items = [], runnerId, emptyState } = {}) => {
      const client = new QueryClient({ defaultOptions: { queries: { staleTime: Infinity } } })
      client.setQueryData(['me'], { id: 'me' })
      client.setQueryData(['follows'], { following, followers: [] })
      client.setQueryData(runnerId ? ['feed', runnerId] : ['feed'], {
        pages: [{ items, nextCursor: null }],
        pageParams: [null],
      })
      const route = createRootRoute({
        component: () => createElement(ActivityFeed, { runnerId, emptyState }),
      })
      const router = createRouter({
        routeTree: route,
        history: createMemoryHistory({ initialEntries: ['/'] }),
      })
      await router.load()
      try {
        return renderToString(
          createElement(
            QueryClientProvider,
            { client },
            createElement(I18nextProvider, { i18n }, createElement(RouterProvider, { router })),
          ),
        )
      } finally {
        client.clear()
      }
    }
    const welcome = await render()
    assert.match(welcome, /Dags för din första aktivitet/)
    assert.match(welcome, /href="\/app\/activities\/new"/)
    assert.match(welcome, /href="\/app\/settings#import-heading"/)
    assert.match(welcome, /Kopiera min profillänk/)
    const following = await render({ following: [{ id: 'friend' }] })
    assert.match(following, /Ingen du följer har delat någon aktivitet ännu/)
    assert.doesNotMatch(following, /Dags för din första aktivitet/)
    const profile = await render({ runnerId: 'friend' })
    assert.match(profile, /Inga aktiviteter/)
    assert.doesNotMatch(profile, /Kopiera min profillänk|Dags för din första aktivitet/)
    const ownProfile = await render({
      runnerId: 'me',
      emptyState: createElement('p', null, 'Own training story'),
    })
    assert.match(ownProfile, /Own training story/)
    assert.doesNotMatch(ownProfile, /Inga aktiviteter|Dags för din första aktivitet/)
    const populated = await render({
      items: [
        {
          id: 'activity',
          userId: 'me',
          displayName: 'Test Runner',
          sport: 'running',
          title: 'Morning run',
          startedAt: '2026-10-31T08:00:00Z',
          durationS: 1800,
          distanceM: 5000,
          routeGeoJson: null,
        },
      ],
    })
    assert.match(populated, /Morning run/)
    assert.doesNotMatch(populated, /Kopiera min profillänk|Dags för din första aktivitet/)
  } finally {
    globalThis.window = previousWindow
    await server.close()
  }
})
