import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import path from 'node:path'
import { defineConfig } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'

// Strikt CSP i den byggda appen (inte i dev, där Vite kör inline-skript för HMR).
// Lägg till origins här när appen börjar hämta från andra, t.ex. kartor från R2.
// frame-ancestors fungerar inte i en meta-tagg och sätts som header där webben hostas.
const contentSecurityPolicy = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self'",
  "img-src 'self' data: blob:",
  "connect-src 'self'",
  "worker-src 'self' blob:",
  "object-src 'none'",
  "base-uri 'none'",
  "form-action 'self'",
].join('; ')

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
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
      includeAssets: ['favicon.svg'],
      manifest: {
        name: 'Kuta',
        short_name: 'Kuta',
        description: 'Logga, analysera och dela dina löppass.',
        lang: 'sv',
        start_url: '/',
        display: 'standalone',
        background_color: '#0b0b0f',
        theme_color: '#0b0b0f',
        icons: [
          { src: 'favicon.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'any' },
          // TODO: lägg till PNG-ikoner (192, 512, maskable) och apple-touch-icon för iOS.
        ],
      },
    }),
  ],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, 'src') },
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
})
