import { LngLatBounds, Map, NavigationControl } from 'maplibre-gl'
import { useEffect, useRef } from 'react'
import { mapStyle } from '@/lib/map'

// Kartbilder från OpenFreeMap (OpenStreetMap-data, ingen nyckel). Leverantören ser vilka kartbitar som hämtas,
// alltså ungefär var passet gick. shortcut: byt till egen PMTiles-fil (AGENTS.md) när det ska stanna hos Kuta.
// Origins för stil, kartbitar, typsnitt och ikoner finns i CSP:n i vite.config.ts.
export function ActivityMap({
  coordinates,
  label,
  routes,
}: {
  coordinates: [number, number][]
  label: string
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
      // Scroll och ett finger flyttar sidan, inte kartan; zooma med Ctrl/Cmd + scroll eller två fingrar
      cooperativeGestures: true,
    })
    map.addControl(new NavigationControl({ showCompass: false }))
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
        id: 'route',
        type: 'line',
        source: 'route',
        layout: { 'line-join': 'round', 'line-cap': 'round' },
        paint: { 'line-color': ['get', 'color'], 'line-width': 4 },
      })
    })
    return () => map.remove()
  }, [coordinates, routes])

  return (
    <div
      ref={container}
      role="img"
      aria-label={label}
      className="h-72 max-h-[45svh] w-full overflow-hidden rounded-lg border sm:h-96"
    />
  )
}
