import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { ActivityCharts, type ChartSeries } from '@/components/activity-charts'
import { ActivityMap } from '@/components/activity-map'
import { ErrorMessage } from '@/components/error-message'
import { ApiError, api } from '@/lib/api'
import {
  activityTitle,
  formatDuration,
  formatPace,
  splits,
  type ActivityDetail,
  type ActivityStreams,
} from '@/lib/activity'

// Tempo ur hastigheten, utjämnat över ~15 s så att GPS-brus inte ger spikar; stillastående blir lucka.
function paceSeries(speed: (number | null)[]) {
  const window = 15
  return speed.map((_, i) => {
    const values = speed
      .slice(Math.max(0, i - window + 1), i + 1)
      .filter((v): v is number => v !== null)
    const avg = values.length ? values.reduce((a, b) => a + b, 0) / values.length : 0
    return avg > 0.5 ? 1000 / avg : null
  })
}

// x-axeln måste vara stigande utan luckor: fyll en saknad distans med föregående.
function distanceKm(streams: ActivityStreams) {
  let last = 0
  return (streams.dist ?? streams.t.map(() => null)).map((d) => (last = d ?? last) / 1000)
}

export function ActivityPage() {
  const { activityId } = useParams({ from: '/app/activities/$activityId' })
  const { t, i18n } = useTranslation()
  const activity = useQuery({
    queryKey: ['activity', activityId],
    queryFn: () => api.get<ActivityDetail>(`/api/activities/${activityId}`),
    retry: (count, error) => !(error instanceof ApiError && error.status === 404) && count < 1,
  })

  const view = useMemo(() => {
    const streams = activity.data?.streams
    if (!streams) return null
    const coordinates = (streams.lat ?? []).flatMap((lat, i): [number, number][] => {
      const lon = streams.lon?.[i]
      return lat != null && lon != null ? [[lon, lat]] : []
    })
    const series: ChartSeries[] = []
    if (streams.alt)
      series.push({
        label: t('activity.chartElevation'),
        values: streams.alt,
        color: '#868e96',
        fill: true,
      })
    if (streams.speed)
      series.push({
        label: t('activity.chartPace'),
        values: paceSeries(streams.speed),
        color: '#1c7ed6',
        pace: true,
      })
    if (streams.hr)
      series.push({ label: t('activity.chartHr'), values: streams.hr, color: '#e03131' })
    return { coordinates, series, distanceKm: distanceKm(streams), splits: splits(streams) }
  }, [activity.data, t])

  if (activity.isPending) return null
  if (activity.isError)
    return (
      <section>
        <BackLink />
        <p role="alert" className="mt-6 text-muted-foreground">
          {activity.error instanceof ApiError && activity.error.status === 404 ? (
            t('activity.notFound')
          ) : (
            <ErrorMessage error={activity.error} />
          )}
        </p>
      </section>
    )

  const a = activity.data
  const number = new Intl.NumberFormat(i18n.resolvedLanguage, {
    maximumFractionDigits: 2,
    minimumFractionDigits: 2,
  })
  const when = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    dateStyle: 'full',
    timeStyle: 'short',
  })
  const stats = [
    [t('activity.distance'), `${number.format(a.distanceM / 1000)} km`],
    [t('activity.time'), formatDuration(a.durationS)],
    [t('activity.pace'), formatPace(a.movingS ?? a.durationS, a.distanceM)],
    a.elevationGainM !== null && [t('activity.elevation'), `${Math.round(a.elevationGainM)} m`],
    a.movingS !== null && [t('activity.movingTime'), formatDuration(a.movingS)],
    a.avgHr !== null && [t('activity.avgHr'), `${a.avgHr} bpm`],
    a.maxHr !== null && [t('activity.maxHr'), `${a.maxHr} bpm`],
  ].filter((s): s is [string, string] => !!s)

  return (
    <section>
      <BackLink />
      <p className="mt-4 text-sm text-muted-foreground">{when.format(new Date(a.startedAt))}</p>
      <h1 className="mt-1 text-2xl font-semibold tracking-tight">{activityTitle(a, t)}</h1>

      <dl className="mt-6 grid grid-cols-3 gap-4">
        {stats.map(([label, value], i) => (
          <div key={label} className={i < 3 ? '' : 'text-sm'}>
            <dt className="text-xs text-muted-foreground">{label}</dt>
            <dd
              className={i < 3 ? 'text-xl font-semibold tabular-nums' : 'font-medium tabular-nums'}
            >
              {value}
            </dd>
          </div>
        ))}
      </dl>

      <div className="mt-8">
        {view && view.coordinates.length > 1 ? (
          <ActivityMap coordinates={view.coordinates} label={t('activity.map')} />
        ) : (
          <p className="text-sm text-muted-foreground">{t('activity.noRoute')}</p>
        )}
      </div>

      {view && view.series.length > 0 && (
        <div className="mt-8">
          <h2 className="mb-2 text-lg font-semibold">{t('activity.charts')}</h2>
          <ActivityCharts distanceKm={view.distanceKm} series={view.series} />
        </div>
      )}

      {view && view.splits.length > 0 && (
        <div className="mt-8">
          <h2 className="mb-2 text-lg font-semibold">{t('activity.splits')}</h2>
          <table className="w-full text-sm tabular-nums">
            <thead className="text-left text-xs text-muted-foreground">
              <tr>
                <th className="py-1 font-normal">{t('activity.splitKm')}</th>
                <th className="py-1 font-normal">{t('activity.splitPace')}</th>
                <th className="py-1 font-normal">{t('activity.splitElevation')}</th>
                {a.streams?.hr && <th className="py-1 font-normal">{t('activity.splitHr')}</th>}
              </tr>
            </thead>
            <tbody className="divide-y">
              {view.splits.map((s) => (
                <tr key={s.km}>
                  <td className="py-1.5">
                    {s.distanceM < 950 ? number.format(s.distanceM / 1000) : s.km}
                  </td>
                  <td className="py-1.5">{formatPace(s.durationS, s.distanceM)}</td>
                  <td className="py-1.5">
                    {s.elevationM === null ? '–' : `${Math.round(s.elevationM)} m`}
                  </td>
                  {a.streams?.hr && <td className="py-1.5">{s.avgHr ?? '–'}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}

function BackLink() {
  const { t } = useTranslation()
  return (
    <Link
      to="/app/activities"
      className="text-sm text-muted-foreground underline-offset-2 hover:underline"
    >
      ← {t('activity.back')}
    </Link>
  )
}
