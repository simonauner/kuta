import { knownSport, sportIcons } from '@/lib/sports'
import { SportIcon } from '@/components/sport-icon'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams, useSearch } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ActivityCharts, type ChartSeries } from '@/components/activity-charts'
import { BackLink } from '@/components/back-link'
import { MultisportEditor } from '@/components/multisport-editor'
import { partStreams, sportNameKey, sportColors, type ActivityPart } from '@/lib/multisport'
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
  formatSportPace,
  splits,
} from '@/lib/activity'

// Tempo ur hastigheten, utjämnat över ~15 s så att GPS-brus inte ger spikar; stillastående blir lucka.
function paceSeries(speed: (number | null)[], minSpeed = 0.5) {
  const window = 15
  return speed.map((_, i) => {
    const values = speed
      .slice(Math.max(0, i - window + 1), i + 1)
      .filter((v): v is number => v !== null)
    const avg = values.length ? values.reduce((a, b) => a + b, 0) / values.length : 0
    return avg > minSpeed ? 1000 / avg : null
  })
}

export function ActivityPage() {
  const { activityId } = useParams({ from: '/app/activities/$activityId' })
  const { t, i18n } = useTranslation()
  const { year, tag } = useSearch({ from: '/app/activities/$activityId' })
  const equipment = useQuery(equipmentQuery)
  const [tab, setTab] = useState<'overview' | 'laps'>('overview')
  const activity = useQuery(activityQuery(activityId))
  const [selection, setSelection] = useState<{ id: string; part: ActivityPart | null } | null>(null)
  const selectedPart = selection?.id === activityId ? selection.part : null
  const selectedSport = selectedPart?.sport ?? activity.data?.sport
  const mixed = (activity.data?.parts.length ?? 0) > 0 && !selectedPart

  const view = useMemo(() => {
    const original = activity.data?.streams
    const streams = original && selectedPart ? partStreams(original, selectedPart) : original
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
    if (streams.speed && !mixed)
      series.push({
        label:
          selectedSport === 'cycling'
            ? `${t('activity.speed')} (km/h)`
            : `${t('activity.chartPace')} (${selectedSport === 'swimming' ? '/100 m' : '/km'})`,
        values:
          selectedSport === 'cycling'
            ? streams.speed.map((v) => (v === null ? null : v * 3.6))
            : paceSeries(streams.speed, selectedSport === 'swimming' ? 0.05 : 0.5).map((v) =>
                v === null ? null : v / (selectedSport === 'swimming' ? 10 : 1),
              ),
        color: '#1c7ed6',
        pace: selectedSport !== 'cycling',
      })
    if (streams.hr)
      series.push({ label: t('activity.chartHr'), values: streams.hr, color: '#e03131' })
    if (streams.cadence && !mixed)
      series.push({ label: t('activity.chartCadence'), values: streams.cadence, color: '#ae3ec9' })
    if (streams.temp)
      series.push({ label: t('activity.chartTemp'), values: streams.temp, color: '#f08c00' })
    const routes =
      !selectedPart && activity.data?.parts.length
        ? activity.data.parts.map((p) => {
            const leg = partStreams(original!, p)
            return {
              color: sportColors[p.sport] ?? '#ae3ec9',
              coordinates: (leg.lat ?? []).flatMap((lat, i): [number, number][] => {
                const lon = leg.lon?.[i]
                return lat != null && lon != null ? [[lon, lat]] : []
              }),
            }
          })
        : undefined
    return {
      coordinates,
      routes,
      streams,
      series,
      axis: chartAxis(streams),
      splits: mixed ? [] : splits(streams),
    }
  }, [activity.data, selectedPart, selectedSport, mixed, t])

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

  const original = activity.data
  const a = selectedPart
    ? {
        ...original,
        sport: selectedPart.sport,
        distanceM: selectedPart.distanceM,
        durationS: selectedPart.endS - selectedPart.startS,
        movingS: null,
        avgHr: average(view?.streams.hr ?? null),
        maxHr: null,
        elevationGainM: null,
        streams: view?.streams ?? null,
        laps: original.laps.filter(
          (l) => l.startS >= selectedPart.startS && l.startS + l.durationS <= selectedPart.endS,
        ),
      }
    : original
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
    !mixed && [
      t(a.sport === 'cycling' ? 'activity.speed' : 'activity.pace'),
      formatSportPace(a.sport, a.movingS ?? a.durationS, a.distanceM, i18n.resolvedLanguage),
    ],
    a.elevationGainM !== null && [t('activity.elevation'), `${Math.round(a.elevationGainM)} m`],
    a.movingS !== null && [t('activity.movingTime'), formatDuration(a.movingS)],
    a.avgHr !== null && [t('activity.avgHr'), `${a.avgHr} bpm`],
    a.maxHr !== null && [t('activity.maxHr'), `${a.maxHr} bpm`],
    !mixed && cadence !== null && [t('activity.avgCadence'), `${cadence} ${cadenceUnit}`],
    temp !== null && [t('activity.avgTemp'), `${temp} °C`],
  ].filter((s): s is [string, string] => !!s)

  return (
    <section>
      <BackLink />
      <p className="mt-4 text-sm text-muted-foreground">{when.format(new Date(a.startedAt))}</p>
      <h1 className="mt-1 flex items-center gap-3 text-2xl font-semibold tracking-tight">
        <span className="inline-block" style={{ viewTransitionName: `activity-title-${a.id}` }}>
          {activityTitle(original, t)}
        </span>
        <SportIcon
          sport={a.sport}
          className="size-9"
          style={{ viewTransitionName: `activity-sport-${a.id}` }}
        />
      </h1>
      {a.device && <p className="mt-1 text-xs text-muted-foreground">{a.device}</p>}

      <MultisportEditor
        key={`${original.id}:${original.partsEdited}:${JSON.stringify(original.parts)}`}
        activity={original}
        onPreview={(part) => {
          setSelection({ id: activityId, part })
          setTab('overview')
        }}
      />
      {selectedPart && (
        <p className="mt-4 text-sm font-medium">
          {t('multisport.selected', {
            number: selectedPart.partIndex + 1,
            sport: t(sportNameKey(selectedPart.sport)),
          })}
        </p>
      )}
      {mixed && (
        <dl
          aria-label={t('multisport.distanceBySport')}
          className="mt-4 flex flex-wrap gap-4 text-sm"
        >
          {[...new Set(original.parts.map((p) => p.sport))].map((sport) => (
            <div key={sport}>
              <dt>{t(sportNameKey(sport))}</dt>
              <dd className="font-semibold">
                {number.format(
                  original.parts
                    .filter((p) => p.sport === sport)
                    .reduce((sum, p) => sum + p.distanceM, 0) / 1000,
                )}{' '}
                km
              </dd>
            </div>
          ))}
        </dl>
      )}

      {a.description && (
        <p className="mt-4 whitespace-pre-wrap text-sm wrap-anywhere">{a.description}</p>
      )}

      <div className="mt-4">
        <Button asChild size="sm" variant="outline">
          <Link
            to="/app/activities/$activityId/edit"
            params={{ activityId: a.id }}
            search={{ year, tag }}
          >
            {t('activity.edit')}
          </Link>
        </Button>
      </div>
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

      {!mixed && a.laps.length > 0 && (
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
                <td className="py-1.5">
                  {formatSportPace(
                    a.sport,
                    l.movingS ?? l.durationS,
                    l.distanceM,
                    i18n.resolvedLanguage,
                  )}
                </td>
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
              <ActivityMap
                coordinates={view.coordinates}
                routes={view.routes}
                label={t('activity.map')}
              />
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
                      <td className="py-1.5">
                        {formatSportPace(a.sport, s.durationS, s.distanceM, i18n.resolvedLanguage)}
                      </td>
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
