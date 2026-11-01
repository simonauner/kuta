import { LngLatBounds, Map, NavigationControl } from 'maplibre-gl'
import { useEffect, useRef } from 'react'
import { mapStyle } from '@/lib/map'

// Map tiles from OpenFreeMap (OpenStreetMap data, no key). The provider sees which tiles are fetched,
// i.e. roughly where the activity went. shortcut: switch to our own PMTiles file (AGENTS.md) when it must stay with Kuta.
// Origins for style, tiles, fonts and icons are in the CSP in vite.config.ts.
export function ActivityMap({
  coordinates,
  label,
  routes,
  thumbnail = false,
}: {
  coordinates: [number, number][]
  label: string
  thumbnail?: boolean
  routes?: { coordinates: [number, number][]; color: string }[]
}) {
  const container = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!container.current || coordinates.length < 2) return
    const bounds = coordinates.reduce(
      (b, c) => b.extend(c),
      new LngLatBounds(coordinates[0], coordinates[0]),
    )
    const map = new Map({
      container: container.current,
      style: mapStyle,
      bounds,
      fitBoundsOptions: { padding: 32 },
      attributionControl: { compact: true },
      // Scroll and one finger move the page, not the map; zoom with Ctrl/Cmd + scroll or two fingers
      cooperativeGestures: !thumbnail,
      interactive: !thumbnail,
    })
    if (!thumbnail) map.addControl(new NavigationControl({ showCompass: false }))
    map.on('load', () => {
      map.addSource('route', {
        type: 'geojson',
        data: {
          type: 'FeatureCollection',
          features: (routes ?? [{ coordinates, color: '#e8590c' }])
            .filter((r) => r.coordinates.length > 1)
            .map((r) => ({
              type: 'Feature',
              properties: { color: r.color },
              geometry: { type: 'LineString', coordinates: r.coordinates },
            })),
        },
      })
      map.addLayer({
        id: 'route-outline',
        type: 'line',
        source: 'route',
        layout: { 'line-join': 'round', 'line-cap': 'round' },
        paint: { 'line-color': '#fff', 'line-width': 7, 'line-opacity': 0.9 },
      })
      map.addLayer({
        id: 'route',
        type: 'line',
        source: 'route',
        layout: { 'line-join': 'round', 'line-cap': 'round' },
        paint: { 'line-color': ['get', 'color'], 'line-width': 4 },
      })
    })
    return () => map.remove()
  }, [coordinates, routes, thumbnail])

  return (
    <div
      ref={container}
      role="img"
      aria-label={label}
      className={
        thumbnail
          ? 'h-52 w-full overflow-hidden rounded-lg border sm:h-60'
          : 'h-72 max-h-[45svh] w-full overflow-hidden rounded-lg border sm:h-96'
      }
    />
  )
}
