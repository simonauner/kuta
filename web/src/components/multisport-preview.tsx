import { isCyclingSport } from '@/lib/sports'
import { useMemo, useState } from 'react'
import { Maximize2, Minimize2 } from 'lucide-react'
import { Button } from './ui/button'
import { useTranslation } from 'react-i18next'
import { ActivityMap } from './activity-map'
import { ActivityCharts, type ChartSeries } from './activity-charts'
import { chartAxis, routeSegments, type ActivityStreams } from '@/lib/activity'
import { nearestTime, partStreams, sportColors, type ActivityPart } from '@/lib/multisport'

export default function MultisportPreview({
  streams,
  parts,
  selected,
  onMoveBoundary,
}: {
  streams: ActivityStreams
  parts: ActivityPart[]
  selected: number | null
  onMoveBoundary?: (index: number, endS: number) => void
}) {
  const { t } = useTranslation()
  const [large, setLarge] = useState(false)
  const view = useMemo(() => {
    const part = selected === null ? null : parts[selected]
    const data = part ? partStreams(streams, part) : streams
    const series: ChartSeries[] = []
    if (data.alt)
      series.push({
        label: t('activity.chartElevation'),
        values: data.alt,
        color: '#868e96',
        fill: true,
      })
    if (data.hr) series.push({ label: t('activity.chartHr'), values: data.hr, color: '#e03131' })
    if (part && data.speed)
      series.push({
        label: isCyclingSport(part.sport)
          ? `${t('activity.speed')} (km/h)`
          : `${t('activity.chartPace')} (${part.sport === 'swimming' ? '/100 m' : '/km'})`,
        values: data.speed.map((v) =>
          v == null || v <= 0
            ? null
            : isCyclingSport(part.sport)
              ? v * 3.6
              : (part.sport === 'swimming' ? 100 : 1000) / v,
        ),
        color: sportColors[part.sport] ?? '#ae3ec9',
        pace: !isCyclingSport(part.sport),
      })
    return {
      coordinates: routeSegments(data).flat(),
      routes: (part ? [part] : parts).flatMap((p) =>
        routeSegments(partStreams(streams, p)).map((coordinates) => ({
          coordinates,
          color: sportColors[p.sport] ?? '#ae3ec9',
        })),
      ),
      series,
      axis: chartAxis(data),
    }
  }, [streams, parts, selected, t])
  const handles = useMemo(
    () =>
      onMoveBoundary &&
      parts.slice(0, -1).flatMap((p, i) => {
        // A zero-length part interpolates the position at the boundary; no GPS there means no handle
        const at = partStreams(streams, { startS: p.endS, endS: p.endS })
        const lat = at.lat?.[0]
        const lon = at.lon?.[0]
        if (lat == null || lon == null) return []
        return [
          {
            position: [lon, lat] as [number, number],
            color: sportColors[parts[i + 1].sport] ?? '#ae3ec9',
            label: t('multisport.boundary', { number: i + 1 }),
            onMove: (to: [number, number]) => {
              const time = nearestTime(streams, to, p.startS, parts[i + 1].endS)
              if (time !== null && time !== p.endS) onMoveBoundary(i, time)
            },
          },
        ]
      }),
    [streams, parts, onMoveBoundary, t],
  )
  return (
    <div className="space-y-4">
      {view.coordinates.length > 1 ? (
        <div className="space-y-2">
          {handles?.length ? (
            <Button
              type="button"
              variant="secondary-outline"
              size="sm"
              aria-pressed={large}
              onClick={() => setLarge((v) => !v)}
            >
              {large ? <Minimize2 /> : <Maximize2 />}
              {large ? t('multisport.mapSmaller') : t('multisport.mapLarger')}
            </Button>
          ) : null}
          <ActivityMap
            coordinates={view.coordinates}
            routes={view.routes}
            fitKey={selected}
            handles={handles}
            large={large}
            label={t('activity.map')}
          />
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">{t('activity.noRoute')}</p>
      )}
      {view.series.length > 0 && <ActivityCharts axis={view.axis} series={view.series} />}
    </div>
  )
}
