import { layers, namedFlavor } from '@protomaps/basemaps'
import i18n from 'i18next'
import { addProtocol, setWorkerUrl, type StyleSpecification } from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?url'
import { Protocol } from 'pmtiles'

// Our own basemap: one PMTiles file (OpenStreetMap data built by Protomaps) plus fonts and sprites, all under
// VITE_MAP_URL (set in vite.config.ts). The browser reads the file with range requests, so no map
// provider learns which area is viewed. The origin is allowed by the CSP in vite.config.ts; see README (Karta).
const mapUrl = new URL(import.meta.env.VITE_MAP_URL, window.location.origin).href.replace(/\/$/, '')
const flavor = namedFlavor('light')

setWorkerUrl(workerUrl)
addProtocol('pmtiles', new Protocol().tile)

export function mapStyle(): StyleSpecification {
  return {
    version: 8,
    glyphs: `${mapUrl}/fonts/{fontstack}/{range}.pbf`,
    sprite: `${mapUrl}/sprites/v4/light`,
    sources: {
      protomaps: {
        type: 'vector',
        url: `pmtiles://${mapUrl}/${import.meta.env.VITE_MAP_TILES}`,
        attribution:
          '<a href="https://protomaps.com">Protomaps</a> © <a href="https://openstreetmap.org/copyright">OpenStreetMap</a>',
      },
    },
    // Labels follow the app language; names without a translation fall back to the local name
    layers: layers('protomaps', flavor, { lang: i18n.resolvedLanguage === 'sv' ? 'sv' : 'en' }),
  }
}
