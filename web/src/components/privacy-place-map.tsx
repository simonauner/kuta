import { Map, NavigationControl, type GeoJSONSource } from 'maplibre-gl'
import { useEffect, useEffectEvent, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { mapStyle } from '@/lib/map'
import { privacyCircle } from '@/lib/privacy-circle'

export default function PrivacyPlaceMap({
  point,
  radiusM,
  onSelect,
  onLocate,
  locating,
}: {
  point: { latitude: number; longitude: number } | null
  radiusM: number
  onSelect: (point: { latitude: number; longitude: number }) => void
  onLocate: () => void
  locating: boolean
}) {
  const { t } = useTranslation()
  const container = useRef<HTMLDivElement>(null)
  const map = useRef<Map | null>(null)
  const [failed, setFailed] = useState(false)
  const select = useEffectEvent(onSelect)
  const draw = useEffectEvent(() => {
    const source = map.current?.getSource<GeoJSONSource>('zone')
    if (!source) return
    source.setData({
      type: 'FeatureCollection',
      features: point
        ? [
            {
              type: 'Feature',
              properties: {},
              geometry: {
                type: 'Polygon',
                coordinates: [privacyCircle(point.latitude, point.longitude, radiusM)],
              },
            },
            {
              type: 'Feature',
              properties: {},
              geometry: { type: 'Point', coordinates: [point.longitude, point.latitude] },
            },
          ]
        : [],
    })
    if (point && !map.current!.getBounds().contains([point.longitude, point.latitude])) {
      map.current!.jumpTo({ center: [point.longitude, point.latitude] })
    }
  })
  const initialPoint = useRef(point)
  useEffect(() => {
    if (!container.current) return
    const initial = initialPoint.current
    const instance = new Map({
      container: container.current,
      style: mapStyle,
      center: initial ? [initial.longitude, initial.latitude] : [18.06, 59.33],
      zoom: initial ? 14 : 10,
      attributionControl: { compact: true },
    })
    map.current = instance
    instance.addControl(new NavigationControl({ showCompass: false }))
    instance.on('error', () => setFailed(true))
    instance.on('click', (event) =>
      select({ latitude: event.lngLat.lat, longitude: event.lngLat.wrap().lng }),
    )
    instance.on('load', () => {
      instance.resize()
      instance.addSource('zone', {
        type: 'geojson',
        data: { type: 'FeatureCollection', features: [] },
      })
      instance.addLayer({
        id: 'zone-fill',
        type: 'fill',
        source: 'zone',
        filter: ['==', '$type', 'Polygon'],
        paint: { 'fill-color': '#e8590c', 'fill-opacity': 0.2 },
      })
      instance.addLayer({
        id: 'zone-line',
        type: 'line',
        source: 'zone',
        filter: ['==', '$type', 'Polygon'],
        paint: { 'line-color': '#e8590c', 'line-width': 2 },
      })
      instance.addLayer({
        id: 'zone-center',
        type: 'circle',
        source: 'zone',
        filter: ['==', '$type', 'Point'],
        paint: {
          'circle-color': '#e8590c',
          'circle-radius': 4,
          'circle-stroke-color': '#fff',
          'circle-stroke-width': 2,
        },
      })
      draw()
    })
    return () => {
      map.current = null
      instance.remove()
    }
  }, [])
  useEffect(() => {
    draw()
  }, [point, radiusM])
  return (
    <div>
      <p id="privacy-map-help" className="mb-2 text-sm text-muted-foreground">
        {t('privacyPlaces.mapHelp')}
      </p>
      <div
        ref={container}
        role="region"
        aria-label={t('privacyPlaces.map')}
        aria-describedby="privacy-map-help"
        className="h-64 overflow-hidden rounded-lg border"
      />
      {failed && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          {t('privacyPlaces.mapUnavailable')}
        </p>
      )}
      <div className="mt-2 flex flex-wrap gap-2">
        <Button
          type="button"
          variant="secondary-outline"
          size="sm"
          onClick={() => {
            const center = map.current?.getCenter().wrap()
            if (center) onSelect({ latitude: center.lat, longitude: center.lng })
          }}
        >
          {t('privacyPlaces.useCenter')}
        </Button>
        <Button
          type="button"
          variant="secondary-outline"
          size="sm"
          disabled={locating}
          onClick={onLocate}
        >
          {t(locating ? 'privacyPlaces.locating' : 'privacyPlaces.useLocation')}
        </Button>
      </div>
    </div>
  )
}
