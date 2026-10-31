import { knownSport, sportIcons } from '@/lib/sports'
import { SportIcon } from '@/components/sport-icon'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams, useSearch } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ActivityCharts, type ChartSeries } from '@/components/activity-charts'
import { BackLink } from '@/components/back-link'
import { Button } from '@/components/ui/button'
import { equipmentQuery } from '@/lib/equipment'
import { TagLabels } from '@/components/activity-tags'
import { ActivityMap } from '@/components/activity-map'
import { ErrorMessage } from '@/components/error-message'
import { ApiError } from '@/lib/api'
import { activityQuery } from '@/lib/activity-query'
import {
  activityTitle,
  chartAxis,
  average,
  formatDuration,
  formatPace,
  splits,
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

export function ActivityPage() {
  const { activityId } = useParams({ from: '/app/activities/$activityId' })
  const { t, i18n } = useTranslation()
  const { year, tag } = useSearch({ from: '/app/activities/$activityId' })
  const equipment = useQuery(equipmentQuery)
  const [tab, setTab] = useState<'overview' | 'laps'>('overview')
  const activity = useQuery(activityQuery(activityId))

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
    if (streams.cadence)
      series.push({ label: t('activity.chartCadence'), values: streams.cadence, color: '#ae3ec9' })
    if (streams.temp)
      series.push({ label: t('activity.chartTemp'), values: streams.temp, color: '#f08c00' })
    return { coordinates, series, axis: chartAxis(streams), splits: splits(streams) }
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
  // Kadens i steg/min för löpning och gång, varv/min för cykling (så lagras den, se FitImport)
  const cadenceUnit = sportIcons[knownSport(a.sport)] === 'bike' ? 'rpm' : 'spm'
  const cadence = average(a.streams?.cadence ?? null)
  const temp = average(a.streams?.temp ?? null)
  const stats = [
    [t('activity.distance'), `${number.format(a.distanceM / 1000)} km`],
    [t('activity.time'), formatDuration(a.durationS)],
    [t('activity.pace'), formatPace(a.movingS ?? a.durationS, a.distanceM)],
    a.elevationGainM !== null && [t('activity.elevation'), `${Math.round(a.elevationGainM)} m`],
    a.movingS !== null && [t('activity.movingTime'), formatDuration(a.movingS)],
    a.avgHr !== null && [t('activity.avgHr'), `${a.avgHr} bpm`],
    a.maxHr !== null && [t('activity.maxHr'), `${a.maxHr} bpm`],
    cadence !== null && [t('activity.avgCadence'), `${cadence} ${cadenceUnit}`],
    temp !== null && [t('activity.avgTemp'), `${temp} °C`],
  ].filter((s): s is [string, string] => !!s)

  return (
    <section>
      <BackLink />
      <p className="mt-4 text-sm text-muted-foreground">{when.format(new Date(a.startedAt))}</p>
      <div className="mt-1 flex flex-wrap items-center justify-between gap-3">
        <h1 className="flex min-w-0 items-center gap-3 text-2xl font-semibold tracking-tight">
          <span className="wrap-anywhere" style={{ viewTransitionName: `activity-title-${a.id}` }}>
            {activityTitle(a, t)}
          </span>
          <SportIcon
            sport={a.sport}
            className="size-9 shrink-0"
            style={{ viewTransitionName: `activity-sport-${a.id}` }}
          />
        </h1>
        <Button asChild size="sm" variant="outline" className="ml-auto">
          <Link
            to="/app/activities/$activityId/edit"
            params={{ activityId: a.id }}
            search={{ year, tag }}
          >
            {t('activity.edit')}
          </Link>
        </Button>
      </div>
      {a.device && <p className="mt-1 text-xs text-muted-foreground">{a.device}</p>}

      {a.description && (
        <p className="mt-4 whitespace-pre-wrap text-sm wrap-anywhere">{a.description}</p>
      )}
      {a.equipmentIds.length > 0 && (
        <div className="mt-5">
          <h2 className="text-sm font-medium">{t('equipment.title')}</h2>
          <ul className="mt-2 flex flex-wrap gap-2">
            {equipment.data
              ?.filter((item) => a.equipmentIds.includes(item.id))
              .map((item) => (
                <li key={item.id} className="rounded-full bg-muted px-2 py-0.5 text-xs">
                  {item.name}
                </li>
              ))}
          </ul>
          {equipment.isPending && (
            <p role="status" className="text-sm text-muted-foreground">
              {t('equipment.loading')}
            </p>
          )}
          {equipment.error && (
            <p role="alert" className="text-sm text-destructive">
              <ErrorMessage error={equipment.error} />
            </p>
          )}
        </div>
      )}
      {a.tags.length > 0 && (
        <div className="mt-5">
          <h2 className="text-sm font-medium">{t('tags.label')}</h2>
          <TagLabels tags={a.tags} />
        </div>
      )}

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

      {a.laps.length > 0 && (
        <div role="tablist" className="mt-8 flex gap-1 border-b">
          {(['overview', 'laps'] as const).map((value) => (
            <button
              key={value}
              role="tab"
              type="button"
              aria-selected={tab === value}
              onClick={() => setTab(value)}
              className={`-mb-px border-b-2 px-3 py-2 text-sm ${tab === value ? 'border-primary font-medium' : 'border-transparent text-muted-foreground'}`}
            >
              {t(`activity.tab.${value}`)}
            </button>
          ))}
        </div>
      )}

      {tab === 'laps' ? (
        <table className="mt-6 w-full text-sm tabular-nums">
          <thead className="text-left text-xs text-muted-foreground">
            <tr>
              <th className="py-1 font-normal">{t('activity.lap')}</th>
              <th className="py-1 font-normal">{t('activity.distance')}</th>
              <th className="py-1 font-normal">{t('activity.time')}</th>
              <th className="py-1 font-normal">{t('activity.pace')}</th>
              <th className="py-1 font-normal">{t('activity.splitElevation')}</th>
              {a.laps.some((l) => l.avgHr !== null) && (
                <th className="py-1 font-normal">{t('activity.splitHr')}</th>
              )}
              {a.laps.some((l) => l.avgCadence !== null) && (
                <th className="py-1 font-normal">{t('activity.cadence')}</th>
              )}
            </tr>
          </thead>
          <tbody className="divide-y">
            {a.laps.map((l) => (
              <tr key={l.lapIndex}>
                <td className="py-1.5">{l.lapIndex + 1}</td>
                <td className="py-1.5">{number.format(l.distanceM / 1000)} km</td>
                <td className="py-1.5">{formatDuration(l.movingS ?? l.durationS)}</td>
                <td className="py-1.5">{formatPace(l.movingS ?? l.durationS, l.distanceM)}</td>
                <td className="py-1.5">
                  {l.ascentM === null && l.descentM === null
                    ? '–'
                    : `${Math.round((l.ascentM ?? 0) - (l.descentM ?? 0))} m`}
                </td>
                {a.laps.some((x) => x.avgHr !== null) && (
                  <td className="py-1.5">{l.avgHr ?? '–'}</td>
                )}
                {a.laps.some((x) => x.avgCadence !== null) && (
                  <td className="py-1.5">{l.avgCadence ?? '–'}</td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <>
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
              <ActivityCharts axis={view.axis} series={view.series} />
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
        </>
      )}
    </section>
  )
}
