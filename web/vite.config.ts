import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { Footer } from './src/components/footer.tsx'
import sv from './src/locales/sv.json' with { type: 'json' }
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import path from 'node:path'
import { defineConfig, type PreviewServer, type ViteDevServer } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'

// Strikt CSP i den byggda appen (inte i dev, där Vite kör inline-skript för HMR).
// Lägg till origins här när appen börjar hämta från andra, t.ex. kartor från R2.
// frame-ancestors fungerar inte i en meta-tagg och sätts som header där webben hostas.
const contentSecurityPolicy = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self'",
  // OpenFreeMap: kartstil, kartbitar, typsnitt och ikoner för passkartan (src/components/activity-map.tsx)
  "img-src 'self' data: blob: https://tiles.openfreemap.org",
  "connect-src 'self' https://tiles.openfreemap.org",
  "worker-src 'self' blob:",
  "object-src 'none'",
  "base-uri 'none'",
  "form-action 'self'",
].join('; ')

const webPort = Number(process.env.KUTA_WEB_PORT ?? 55300)
const apiPort = Number(process.env.KUTA_API_PORT ?? 55302)

// Samma dokumentvägar i Vites utvecklingsserver och förhandsvisning som i .NET.
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
    next()
  })
}

// https://vite.dev/config/
export default defineConfig({
  build: {
    rolldownOptions: {
      input: {
        landing: path.resolve(import.meta.dirname, 'index.html'),
        privacy: path.resolve(import.meta.dirname, 'privacy.html'),
        terms: path.resolve(import.meta.dirname, 'terms.html'),
        features: path.resolve(import.meta.dirname, 'features.html'),
        app: path.resolve(import.meta.dirname, 'app.html'),
      },
    },
  },
  define: {
    'import.meta.env.VITE_APP_VERSION': JSON.stringify(
      new Date().toISOString().replace(/[-:]/g, '').slice(0, 15).replace('T', '.'),
    ),
  },
  plugins: [
    react(),
    {
      name: 'public-footer',
      transformIndexHtml: (html) =>
        html.replace(
          '<!--kuta-footer-->',
          renderToStaticMarkup(createElement(Footer, { variant: 'public', labels: sv.footer })),
        ),
    },
    {
      name: 'app-html-routes',
      configureServer: configureHtmlRoutes,
      configurePreviewServer: configureHtmlRoutes,
    },
    {
      name: 'content-security-policy',
      apply: 'build',
      transformIndexHtml: () => [
        {
          tag: 'meta',
          attrs: { 'http-equiv': 'Content-Security-Policy', content: contentSecurityPolicy },
          injectTo: 'head-prepend',
        },
      ],
    },
    tailwindcss(),
    VitePWA({
      registerType: 'autoUpdate',
      // Bara appens navigeringar får appskalet; startsidan och API:t ska nå servern.
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
        // Behåll identiteten för redan installerade PWA:er när startadressen flyttas.
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
  // Kuta håller sig till 553xx lokalt (Supabase 55320–55329) så att det kan köras bredvid andra projekt.
  // scripts/run-dev.sh --ports flyttar serien för att köra flera instanser samtidigt.
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
})
