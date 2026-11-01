import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { Footer } from './src/components/footer.tsx'
import sv from './src/locales/sv.json' with { type: 'json' }
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import path from 'node:path'
import { defineConfig, type PreviewServer, type ViteDevServer } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'

// Strict CSP in the built app (not in dev, where Vite runs inline scripts for HMR).
// Add origins here when the app starts fetching from others, e.g. maps from R2.
// frame-ancestors doesn't work in a meta tag and is set as a header where the web is hosted.
const contentSecurityPolicy = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self'",
  // OpenFreeMap: map style, tiles, fonts and icons for the activity map (src/components/activity-map.tsx)
  "img-src 'self' data: blob: https://tiles.openfreemap.org",
  "connect-src 'self' https://tiles.openfreemap.org",
  "worker-src 'self' blob:",
  "object-src 'none'",
  "base-uri 'none'",
  "form-action 'self'",
].join('; ')

const webPort = Number(process.env.KUTA_WEB_PORT ?? 55300)
const apiPort = Number(process.env.KUTA_API_PORT ?? 55302)

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
})
