import {
  LngLatBounds,
  Map,
  Marker,
  NavigationControl,
  type GeoJSONSource,
  type GeoJSONSourceSpecification,
} from 'maplibre-gl'
import { useEffect, useLayoutEffect, useMemo, useRef } from 'react'
import { mapStyle } from '@/lib/map'

// Basemap from our own PMTiles file (src/lib/map.ts), so no map provider sees where the activity went.
export function ActivityMap({
  coordinates,
  label,
  routes,
  thumbnail = false,
  large = false,
  fitKey,
  handles,
}: {
  coordinates: [number, number][]
  label: string
  thumbnail?: boolean
  // Wider and taller map for precise dragging of handles; MapLibre follows the container size itself
  large?: boolean
  routes?: { coordinates: [number, number][]; color: string }[]
  // Refit to the route only when this changes (default: when coordinates change), so editing keeps the zoom
  fitKey?: unknown
  // Draggable points on the route; onMove gets the pointer position and the parent decides where the handle goes
  handles?: {
    position: [number, number]
    color: string
    label: string
    onMove: (to: [number, number]) => void
  }[]
}) {
  const container = useRef<HTMLDivElement>(null)
  const mapRef = useRef<Map | null>(null)
  const data = useMemo(
    (): GeoJSONSourceSpecification['data'] => ({
      type: 'FeatureCollection',
      features: (routes ?? [{ coordinates, color: '#e8590c' }])
        .filter((r) => r.coordinates.length > 1)
        .map((r) => ({
          type: 'Feature',
          properties: { color: r.color },
          geometry: { type: 'LineString', coordinates: r.coordinates },
        })),
    }),
    [coordinates, routes],
  )
  const markers = useRef<Marker[]>([])
  const latest = useRef({ coordinates, data, handles })
  useLayoutEffect(() => {
    latest.current = { coordinates, data, handles }
  })

  // The map is created once; route changes update the source instead of recreating it (which reset the zoom)
  useEffect(() => {
    if (!container.current) return
    const map = new Map({
      container: container.current,
      style: mapStyle(),
      attributionControl: { compact: true },
      // Scroll and one finger move the page, not the map; zoom with Ctrl/Cmd + scroll or two fingers
      cooperativeGestures: !thumbnail,
      interactive: !thumbnail,
    })
    if (!thumbnail) map.addControl(new NavigationControl({ showCompass: false }))
    // Thumbnails can't be dragged, which is what collapses the attribution otherwise. OSM's attribution guidelines
    // allow collapsing behind the (i) button once it has been shown for five seconds.
    let collapse: ReturnType<typeof setTimeout> | undefined
    if (thumbnail)
      map.once('load', () => {
        collapse = setTimeout(
          () =>
            container.current
              ?.querySelector('.maplibregl-ctrl-attrib')
              ?.classList.remove('maplibregl-compact-show'),
          5000,
        )
      })
    map.on('load', () => {
      map.addSource('route', { type: 'geojson', data: latest.current.data })
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
    mapRef.current = map
    return () => {
      clearTimeout(collapse)
      markers.current.forEach((m) => m.remove())
      markers.current = []
      map.remove()
      mapRef.current = null
    }
  }, [thumbnail])

  useEffect(() => {
    mapRef.current?.getSource<GeoJSONSource>('route')?.setData(data)
  }, [data])

  useEffect(() => {
    const map = mapRef.current
    const list = handles ?? []
    if (!map) return
    if (markers.current.length !== list.length) {
      markers.current.forEach((m) => m.remove())
      markers.current = list.map((handle, i) => {
        const element = document.createElement('div')
        element.className = 'size-5 cursor-grab rounded-full border-2 border-white shadow-md'
        // The slider next to each part is the keyboard alternative
        element.setAttribute('aria-hidden', 'true')
        const marker = new Marker({ element, draggable: true })
          .setLngLat(handle.position)
          .addTo(map)
        marker.on('drag', () => {
          const { lng, lat } = marker.getLngLat()
          latest.current.handles?.[i]?.onMove([lng, lat])
        })
        // Snap back onto the route where the pointer let go
        marker.on('dragend', () => {
          const handle = latest.current.handles?.[i]
          if (handle) marker.setLngLat(handle.position)
        })
        return marker
      })
    }
    list.forEach((handle, i) => {
      const marker = markers.current[i]
      marker.setLngLat(handle.position)
      marker.getElement().style.backgroundColor = handle.color
      marker.getElement().title = handle.label
    })
  }, [handles, thumbnail])

  const fit = fitKey === undefined ? coordinates : fitKey
  useEffect(() => {
    const { coordinates } = latest.current
    if (!mapRef.current || coordinates.length < 2) return
    const bounds = coordinates.reduce(
      (b, c) => b.extend(c),
      new LngLatBounds(coordinates[0], coordinates[0]),
    )
    mapRef.current.fitBounds(bounds, { padding: 32, duration: 0 })
  }, [fit, thumbnail])

  return (
    <div
      role="img"
      aria-label={label}
      className={
        thumbnail
          ? 'h-full w-full overflow-hidden'
          : // Negative margins widen the map past the form column, centred and kept inside the viewport.
            // Width follows the margins (no w-full) so the size change can animate.
            `overflow-hidden rounded-lg border transition-[height,max-height,margin] duration-300 motion-reduce:transition-none ${
              large
                ? 'mx-[calc(50%-min(50vw-1rem,48rem))] h-[60svh] max-h-[60svh]'
                : 'mx-0 h-72 max-h-[45svh] sm:h-96'
            }`
      }
    >
      {/* MapLibre adds its own classes here, so this element's className must never change */}
      <div ref={container} className="size-full" />
    </div>
  )
}
