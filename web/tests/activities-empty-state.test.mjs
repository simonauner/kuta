import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createServer } from 'vite'
import { createElement } from 'react'
import { renderToString } from 'react-dom/server'
import {
  createRootRoute,
  createRouter,
  createMemoryHistory,
  RouterProvider,
} from '@tanstack/react-router'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import sv from '../src/locales/sv.json' with { type: 'json' }
import en from '../src/locales/en.json' with { type: 'json' }

test('each empty page says what will appear there and keeps the same first actions in both languages', async () => {
  const server = await createServer({
    configFile: false,
    root: new URL('..', import.meta.url).pathname,
    resolve: { alias: { '@': new URL('../src', import.meta.url).pathname } },
    server: { middlewareMode: true, watch: null, ws: false },
    appType: 'custom',
  })
  try {
    const { ActivitiesEmptyState } = await server.ssrLoadModule(
      '/src/components/activities-empty-state.tsx',
    )
    const i18n = createInstance()
    await i18n.init({ lng: 'sv', resources: { sv: { translation: sv }, en: { translation: en } } })
    const contexts = ['activities', 'stats', 'profile']
    for (const [language, translations] of Object.entries({ sv, en })) {
      await i18n.changeLanguage(language)
      for (const context of contexts) {
        const route = createRootRoute({
          component: () => createElement(ActivitiesEmptyState, { context }),
        })
        const router = createRouter({
          routeTree: route,
          history: createMemoryHistory({ initialEntries: ['/'] }),
        })
        await router.load()
        const html = renderToString(
          createElement(I18nextProvider, { i18n }, createElement(RouterProvider, { router })),
        )
        for (const other of contexts)
          assert.equal(html.includes(translations.emptyState[other].title), other === context)
        assert.ok(html.includes(translations.onboarding.addActivity))
        assert.ok(html.includes(translations.settings.strava))
        assert.match(html, /href="\/app\/activities\/new"/)
        assert.match(html, /href="\/app\/settings#import-heading"/)
      }
    }
  } finally {
    await server.close()
  }
})

test('statistics only offers onboarding after confirming the entire activity history is empty', async () => {
  const { QueryClient, QueryClientProvider } = await import('@tanstack/react-query')
  const server = await createServer({
    configFile: false,
    root: new URL('..', import.meta.url).pathname,
    resolve: { alias: { '@': new URL('../src', import.meta.url).pathname } },
    server: { middlewareMode: true, watch: null, ws: false },
    appType: 'custom',
  })
  try {
    const { StatsPage } = await server.ssrLoadModule('/src/routes/stats.tsx')
    const i18n = createInstance()
    await i18n.init({ lng: 'sv', resources: { sv: { translation: sv } } })
    const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
    for (const years of [undefined, [], [new Date().getFullYear() - 1]]) {
      const client = new QueryClient({ defaultOptions: { queries: { staleTime: Infinity } } })
      if (years !== undefined) client.setQueryData(['activities', 'years', timeZone], years)
      client.setQueryData(
        ['activities', 'statistics', timeZone, new Date().getFullYear(), 'running'],
        [],
      )
      const route = createRootRoute({ component: StatsPage })
      const router = createRouter({
        routeTree: route,
        history: createMemoryHistory({ initialEntries: ['/'] }),
      })
      await router.load()
      try {
        const html = renderToString(
          createElement(
            QueryClientProvider,
            { client },
            createElement(I18nextProvider, { i18n }, createElement(RouterProvider, { router })),
          ),
        )
        assert.equal(html.includes(sv.emptyState.stats.title), years?.length === 0)
        assert.equal(html.includes(sv.stats.empty), years !== undefined && years.length > 0)
        assert.equal(html.includes('<select'), years !== undefined && years.length > 0)
      } finally {
        client.clear()
      }
    }
  } finally {
    await server.close()
  }
})
