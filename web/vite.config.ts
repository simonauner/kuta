import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { Footer } from './src/components/footer.tsx'
import en from './src/locales/en.json' with { type: 'json' }
import sv from './src/locales/sv.json' with { type: 'json' }
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { createReadStream, statSync } from 'node:fs'
import path from 'node:path'
import { defineConfig, loadEnv, type PreviewServer, type ViteDevServer } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'

// Strict CSP in the built app (not in dev, where Vite runs inline scripts for HMR).
// Add origins here when the app starts fetching from others.
// frame-ancestors doesn't work in a meta tag and is set as a header where the web is hosted.
const contentSecurityPolicy = (mapOrigin: string) =>
  [
    "default-src 'self'",
    // Cloudflare Turnstile on the sign-up and password forms (src/components/turnstile.tsx); loaded only when the API
    // has a site key. The app is one page, so the origin is allowed throughout it.
    "script-src 'self' https://challenges.cloudflare.com",
    'frame-src https://challenges.cloudflare.com',
    "style-src 'self'",
    // Our basemap (VITE_MAP_URL, src/lib/map.ts): PMTiles range requests, fonts and sprites
    `img-src 'self' data: blob: ${mapOrigin}`.trim(),
    `connect-src 'self' ${mapOrigin}`.trim(),
    "worker-src 'self' blob:",
    "object-src 'none'",
    "base-uri 'none'",
    "form-action 'self'",
  ].join('; ')

const webPort = Number(process.env.KUTA_WEB_PORT ?? 55300)
const apiPort = Number(process.env.KUTA_API_PORT ?? 55302)

// Local basemap from scripts/fetch-map.sh, served under /map-dev with range requests like R2 (only dev and preview).
const mapDevDir = path.resolve(import.meta.dirname, '.map-dev')
function serveDevMap(server: ViteDevServer | PreviewServer) {
  server.middlewares.use('/map-dev', (req, res, next) => {
    const file = path.join(
      mapDevDir,
      decodeURIComponent(new URL(req.url ?? '/', 'http://localhost').pathname),
    )
    if (!file.startsWith(mapDevDir + path.sep)) return next()
    let size: number
    try {
      size = statSync(file).size
    } catch {
      res.statusCode = 404
      return res.end()
    }
    const range = /^bytes=(\d+)-(\d*)$/.exec(req.headers.range ?? '')
    const start = range ? Number(range[1]) : 0
    const end = range?.[2] ? Math.min(Number(range[2]), size - 1) : size - 1
    if (start > end) {
      res.statusCode = 416
      return res.end()
    }
    res.statusCode = range ? 206 : 200
    res.setHeader('Accept-Ranges', 'bytes')
    res.setHeader('Content-Length', end - start + 1)
    if (range) res.setHeader('Content-Range', `bytes ${start}-${end}/${size}`)
    createReadStream(file, { start, end }).pipe(res)
  })
}

// Same document paths in Vite's dev server and preview as in .NET.
function configureHtmlRoutes(server: ViteDevServer | PreviewServer) {
  server.middlewares.use((req, _res, next) => {
    const url = new URL(req.url ?? '/', 'http://localhost')
    if (url.pathname === '/app' || url.pathname.startsWith('/app/'))
      req.url = `/app.html${url.search}`
    else if (url.pathname === '/privacy' || url.pathname === '/privacy/')
      req.url = `/privacy.html${url.search}`
    else if (url.pathname === '/terms' || url.pathname === '/terms/')
      req.url = `/terms.html${url.search}`
    else if (url.pathname === '/features' || url.pathname === '/features/')
      req.url = `/features.html${url.search}`
    else if (url.pathname === '/en' || url.pathname === '/en/')
      req.url = `/en/index.html${url.search}`
    else if (url.pathname === '/en/features' || url.pathname === '/en/features/')
      req.url = `/en/features.html${url.search}`
    next()
  })
}

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // Basemap (src/lib/map.ts, README Karta): the R2 bucket in builds, the local extract from scripts/fetch-map.sh in dev.
  // VITE_MAP_URL and VITE_MAP_TILES in the environment or .env.[mode].local override, e.g. /map-dev for a preview.
  const mapEnv = loadEnv(mode, import.meta.dirname, 'VITE_MAP_')
  const production = mode === 'production'
  const mapUrl = mapEnv.VITE_MAP_URL ?? (production ? 'https://tiles.kutaapp.com' : '/map-dev')
  const mapTiles =
    mapEnv.VITE_MAP_TILES ?? (production ? 'nordics-20261102.pmtiles' : 'basemap.pmtiles')
  // A relative VITE_MAP_URL is the same origin, which 'self' already allows
  const mapOrigin = URL.canParse(mapUrl) ? new URL(mapUrl).origin : ''
  return {
    build: {
      rolldownOptions: {
        input: {
          landing: path.resolve(import.meta.dirname, 'index.html'),
          privacy: path.resolve(import.meta.dirname, 'privacy.html'),
          terms: path.resolve(import.meta.dirname, 'terms.html'),
          features: path.resolve(import.meta.dirname, 'features.html'),
          'en/landing': path.resolve(import.meta.dirname, 'en/index.html'),
          'en/features': path.resolve(import.meta.dirname, 'en/features.html'),
          app: path.resolve(import.meta.dirname, 'app.html'),
        },
      },
    },
    define: {
      'import.meta.env.VITE_MAP_URL': JSON.stringify(mapUrl),
      'import.meta.env.VITE_MAP_TILES': JSON.stringify(mapTiles),
      'import.meta.env.VITE_APP_VERSION': JSON.stringify(
        new Date().toISOString().replace(/[-:]/g, '').slice(0, 15).replace('T', '.'),
      ),
    },
    plugins: [
      react(),
      {
        name: 'public-footer',
        transformIndexHtml: (html, { path }) => {
          const language = path.startsWith('/en/') ? 'en' : 'sv'
          const labels = language === 'en' ? en.footer : sv.footer
          return html.replace(
            '<!--kuta-footer-->',
            renderToStaticMarkup(createElement(Footer, { variant: 'public', labels, language })),
          )
        },
      },
      {
        name: 'app-html-routes',
        configureServer: configureHtmlRoutes,
        configurePreviewServer: configureHtmlRoutes,
      },
      {
        name: 'dev-map',
        configureServer: serveDevMap,
        configurePreviewServer: serveDevMap,
      },
      {
        name: 'content-security-policy',
        apply: 'build',
        transformIndexHtml: () => [
          {
            tag: 'meta',
            attrs: {
              'http-equiv': 'Content-Security-Policy',
              content: contentSecurityPolicy(mapOrigin),
            },
            injectTo: 'head-prepend',
          },
        ],
      },
      tailwindcss(),
      VitePWA({
        registerType: 'autoUpdate',
        // Only app navigations get the app shell; the landing page and the API must reach the server.
        workbox: {
          navigateFallback: '/app.html',
          navigateFallbackAllowlist: [
            /^\/app(?:\/(?:activities|stats|profile|runners\/[0-9a-f-]{36}|settings(?:\/(?:equipment|followers|privacy-places))?))?\/?(?:\?|$)/,
          ],
        },
        includeAssets: [
          'favicon.svg',
          'favicon-32.png',
          'apple-touch-icon.png',
          'icons/*.png',
          'brand/*.svg',
        ],
        manifest: {
          name: 'Kuta',
          short_name: 'Kuta',
          description: 'Logga, analysera och dela dina löppass.',
          lang: 'sv',
          // Keep the identity of already installed PWAs when the start URL moves.
          id: '/',
          start_url: '/app',
          display: 'standalone',
          background_color: '#0b0b0f',
          theme_color: '#0b0b0f',
          icons: [
            { src: 'favicon.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'any' },
            { src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
            { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
            {
              src: '/icons/icon-maskable-512.png',
              sizes: '512x512',
              type: 'image/png',
              purpose: 'maskable',
            },
          ],
        },
      }),
    ],
    resolve: {
      alias: { '@': path.resolve(import.meta.dirname, 'src') },
    },
    // Kuta stays within 553xx locally (Supabase 55320–55329) so it can run alongside other projects.
    // scripts/run-dev.sh --ports shifts the range to run several instances at once.
    server: {
      port: webPort,
      strictPort: true,
      proxy: {
        '/api': `http://localhost:${apiPort}`,
      },
    },
    preview: {
      port: webPort + 1,
      strictPort: true,
    },
  }
})
