import { knownSport, sportIcons } from '@/lib/sports'
import { SportIcon } from '@/components/sport-icon'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams, useSearch } from '@tanstack/react-router'
import { useId, useMemo, useState } from 'react'
import { ChevronDown } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ActivityCharts, type ChartSeries } from '@/components/activity-charts'
import { BackLink } from '@/components/back-link'
import { MultisportEditor } from '@/components/multisport-editor'
import { partStreams, sportNameKey, sportColors, type ActivityPart } from '@/lib/multisport'
import { Button } from '@/components/ui/button'
import { meQuery } from '@/lib/auth'
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
  routeSegments,
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
  const { data: me } = useQuery(meQuery)
  const equipment = useQuery(equipmentQuery)
  const [openPanel, setOpenPanel] = useState<'laps' | 'splits' | null>(null)
  const lapsOpen = openPanel === 'laps'
  const splitsOpen = openPanel === 'splits'
  const lapsId = useId()
  const splitsId = useId()
  const activity = useQuery(activityQuery(activityId))
  const [selection, setSelection] = useState<{ id: string; part: ActivityPart | null } | null>(null)
  const selectedPart = selection?.id === activityId ? selection.part : null
  const selectedSport = selectedPart?.sport ?? activity.data?.sport
  const mixed = (activity.data?.parts.length ?? 0) > 0 && !selectedPart

  const view = useMemo(() => {
    const original = activity.data?.streams
    const streams = original && selectedPart ? partStreams(original, selectedPart) : original
    if (!streams) return null
    const segments = routeSegments(streams)
    const coordinates = segments.flat()
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
        ? activity.data.parts.flatMap((p) => {
            const leg = partStreams(original!, p)
            return routeSegments(leg).map((coordinates) => ({
              color: sportColors[p.sport] ?? '#ae3ec9',
              coordinates,
            }))
          })
        : segments.map((coordinates) => ({ coordinates, color: '#e8590c' }))
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

  const splitTable = view && (
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
  )

  return (
    <section>
      {original.userId === me?.id ? (
        <BackLink />
      ) : (
        <Link
          to="/app"
          className="text-sm text-muted-foreground underline-offset-2 hover:underline"
        >
          ← {t('nav.feed')}
        </Link>
      )}
      <p className="mt-4 text-sm text-muted-foreground">{when.format(new Date(a.startedAt))}</p>
      <div className="mt-1 flex flex-wrap items-center justify-between gap-3">
        <h1 className="flex min-w-0 items-center gap-3 text-2xl font-semibold tracking-tight">
          <span className="wrap-anywhere" style={{ viewTransitionName: `activity-title-${a.id}` }}>
            {activityTitle(original, t)}
          </span>
          <SportIcon
            sport={a.sport}
            className="size-9 shrink-0"
            style={{ viewTransitionName: `activity-sport-${a.id}` }}
          />
        </h1>
        {original.userId === me?.id && (
          <Button asChild size="sm" variant="outline" className="ml-auto">
            <Link
              to="/app/activities/$activityId/edit"
              params={{ activityId: a.id }}
              search={{ year, tag }}
            >
              {t('activity.edit')}
            </Link>
          </Button>
        )}
      </div>
      {original.userId !== me?.id && (
        <p className="mt-3 text-sm text-muted-foreground">
          {t('social.sharedDetail', { name: original.displayName })}
        </p>
      )}
      {a.device && <p className="mt-1 text-xs text-muted-foreground">{a.device}</p>}

      {a.description && (
        <p className="mt-4 whitespace-pre-wrap text-sm wrap-anywhere">{a.description}</p>
      )}
      <MultisportEditor
        key={`${original.id}:${original.partsEdited}:${JSON.stringify(original.parts)}`}
        activity={original}
        onPreview={(part) => {
          setSelection({ id: activityId, part })
          setOpenPanel(null)
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

      {((!mixed && a.laps.length > 0) || (view && view.splits.length > 0)) && (
        <div className="mt-8">
          <div className="flex gap-2">
            {!mixed && a.laps.length > 0 && (
              <Button
                id={`${lapsId}-toggle`}
                type="button"
                variant="outline"
                aria-expanded={lapsOpen}
                aria-controls={lapsId}
                onClick={() => setOpenPanel((open) => (open === 'laps' ? null : 'laps'))}
              >
                {t('activity.tab.laps')}
                <ChevronDown
                  aria-hidden="true"
                  className={`transition-transform duration-250 motion-reduce:transition-none ${lapsOpen ? 'rotate-180' : ''}`}
                />
              </Button>
            )}
            {view && view.splits.length > 0 && (
              <Button
                id={`${splitsId}-toggle`}
                type="button"
                variant="outline"
                aria-expanded={splitsOpen}
                aria-controls={splitsId}
                onClick={() => setOpenPanel((open) => (open === 'splits' ? null : 'splits'))}
              >
                {t('activity.splits')}
                <ChevronDown
                  aria-hidden="true"
                  className={`transition-transform duration-250 motion-reduce:transition-none ${splitsOpen ? 'rotate-180' : ''}`}
                />
              </Button>
            )}
          </div>
          {!mixed && a.laps.length > 0 && (
            <div
              id={lapsId}
              aria-hidden={!lapsOpen}
              inert={!lapsOpen}
              className={`grid transition-[grid-template-rows] duration-250 ease-out motion-reduce:transition-none ${lapsOpen ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]'}`}
            >
              <div className="min-h-0 overflow-hidden">
                <div
                  role="region"
                  aria-labelledby={`${lapsId}-toggle`}
                  tabIndex={lapsOpen ? 0 : -1}
                  className="mt-3 max-h-[min(16rem,25svh)] overflow-auto overscroll-contain rounded-lg border px-3 focus-visible:outline-2 focus-visible:outline-ring"
                >
                  <table className="w-full text-sm whitespace-nowrap tabular-nums [&_td]:pr-4 [&_th]:pr-4">
                    <thead className="sticky top-0 bg-background text-left text-xs text-muted-foreground">
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
                </div>
              </div>
            </div>
          )}

          {view && view.splits.length > 0 && (
            <div
              id={splitsId}
              aria-hidden={!splitsOpen}
              inert={!splitsOpen}
              className={`grid transition-[grid-template-rows] duration-250 ease-out motion-reduce:transition-none ${splitsOpen ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]'}`}
            >
              <div className="min-h-0 overflow-hidden">
                <div
                  role="region"
                  aria-labelledby={`${splitsId}-toggle`}
                  tabIndex={splitsOpen ? 0 : -1}
                  className="mt-3 max-h-[min(16rem,25svh)] overflow-auto overscroll-contain rounded-lg border px-3 focus-visible:outline-2 focus-visible:outline-ring [&_thead]:sticky [&_thead]:top-0 [&_thead]:bg-background"
                >
                  {splitTable}
                </div>
              </div>
            </div>
          )}
        </div>
      )}

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
          {splitTable}
        </div>
      )}
    </section>
  )
}
