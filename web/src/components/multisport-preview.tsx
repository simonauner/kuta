import { isCyclingSport } from '@/lib/sports'
import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { ActivityMap } from './activity-map'
import { ActivityCharts, type ChartSeries } from './activity-charts'
import { chartAxis, routeSegments, type ActivityStreams } from '@/lib/activity'
import { partStreams, sportColors, type ActivityPart } from '@/lib/multisport'

export default function MultisportPreview({
  streams,
  parts,
  selected,
}: {
  streams: ActivityStreams
  parts: ActivityPart[]
  selected: number | null
}) {
  const { t } = useTranslation()
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
  return (
    <div className="space-y-4">
      {view.coordinates.length > 1 ? (
        <ActivityMap
          coordinates={view.coordinates}
          routes={view.routes}
          label={t('activity.map')}
        />
      ) : (
        <p className="text-sm text-muted-foreground">{t('activity.noRoute')}</p>
      )}
      {view.series.length > 0 && <ActivityCharts axis={view.axis} series={view.series} />}
    </div>
  )
}
