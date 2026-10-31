import { setWorkerUrl } from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?url'

// Origins for styles, tiles and fonts are allowed by the CSP in vite.config.ts.
export const mapStyle = 'https://tiles.openfreemap.org/styles/liberty'
setWorkerUrl(workerUrl)
