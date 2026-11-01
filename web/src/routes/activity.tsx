import { SlowLoading } from '@/components/slow-loading'
import { isCyclingSport, knownSport, sportIcons } from '@/lib/sports'
import { SportIcon } from '@/components/sport-icon'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useId, useMemo, useState } from 'react'
import { ChevronDown } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ActivityCharts, type ChartSeries } from '@/components/activity-charts'
import { BackLink } from '@/components/back-link'
import { MultisportEditor } from '@/components/multisport-editor'
import { partStreams, sportNameKey, sportColors, type ActivityPart } from '@/lib/multisport'
import { Button } from '@/components/ui/button'
import { Heading } from '@/components/ui/heading'
import { meQuery } from '@/lib/auth'
import { equipmentQuery } from '@/lib/equipment'
import { TagLabels } from '@/components/activity-tags'
import { ActivityMap } from '@/components/activity-map'
import { ErrorMessage } from '@/components/error-message'
import { RecordAward } from '@/components/record-award'
import { ApiError } from '@/lib/api'
import { activityQuery, refreshActivityQueries } from '@/lib/activity-query'
import { api } from '@/lib/api'
import {
  recordMeters,
  type RecordDistance,
  type StoredBestEffort,
  type ActivityBestEffort,
} from '@/lib/records'
import {
  activityTitle,
  chartAxis,
  average,
  formatDuration,
  formatPace,
  formatSportPace,
  splits,
  routeSegments,
} from '@/lib/activity'
import { card } from '@/components/ui/card-variants'
import { VisitorNotice } from '@/components/visitor-notice'
import { ConfirmationModal } from '@/components/ui/confirmation-modal'

// Pace from speed, smoothed over ~15 s so GPS noise doesn't cause spikes; standing still becomes a gap.
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
  const search = useSearch({ from: '/app/activities/$activityId' })
  const { data: me } = useQuery(meQuery)
  const visitor = me === null
  const equipment = useQuery({ ...equipmentQuery, enabled: !visitor })
  const [openPanel, setOpenPanel] = useState<'laps' | 'splits' | 'efforts' | null>(null)
  const lapsOpen = openPanel === 'laps'
  const splitsOpen = openPanel === 'splits'
  const effortsOpen = openPanel === 'efforts'
  const lapsId = useId()
  const splitsId = useId()
  const effortsId = useId()
  const activity = useQuery(activityQuery(activityId, visitor))
  const bestEfforts = useQuery({
    queryKey: ['records', 'activity', activityId],
    queryFn: () => api.get<ActivityBestEffort[]>(`/api/activities/${activityId}/best-efforts`),
    enabled: activity.isSuccess && !visitor,
  })
  // Record badges only for own activities: /api/records covers the signed-in user's records.
  const recordYear = activity.data && new Date(activity.data.startedAt).getUTCFullYear()
  const hasEfforts = !!bestEfforts.data?.length
  const ownEfforts = hasEfforts && activity.data?.userId === me?.id
  const yearBests = useQuery({
    queryKey: ['records', 'year', recordYear],
    queryFn: () => api.get<StoredBestEffort[]>(`/api/records?year=${recordYear}`),
    enabled: ownEfforts,
  })
  const holds = (records: StoredBestEffort[] | undefined, e: StoredBestEffort) =>
    !!records?.some((r) => r.activityId === e.activityId && r.distanceM === e.distanceM)
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
        label: isCyclingSport(selectedSport ?? '')
          ? `${t('activity.speed')} (km/h)`
          : `${t('activity.chartPace')} (${selectedSport === 'swimming' ? '/100 m' : '/km'})`,
        values: isCyclingSport(selectedSport ?? '')
          ? streams.speed.map((v) => (v === null ? null : v * 3.6))
          : paceSeries(streams.speed, selectedSport === 'swimming' ? 0.05 : 0.5).map((v) =>
              v === null ? null : v / (selectedSport === 'swimming' ? 10 : 1),
            ),
        color: '#1c7ed6',
        pace: !isCyclingSport(selectedSport ?? ''),
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
  if (visitor && activity.error instanceof ApiError && activity.error.status === 404)
    return <VisitorNotice kind="activity" />
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
  // Garmin requires attribution in every expanded view and sub-view, not just below the title
  const sourceNote = a.attribution && (
    <p className="mb-2 text-xs text-muted-foreground">{a.attribution}</p>
  )
  const number = new Intl.NumberFormat(i18n.resolvedLanguage, {
    maximumFractionDigits: 2,
    minimumFractionDigits: 2,
  })
  const when = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    dateStyle: 'full',
    timeStyle: 'short',
  })
  // Cadence in steps/min for running and walking, rpm for cycling (as stored, see FitImport)
  const cadenceUnit = isCyclingSport(a.sport) ? 'rpm' : 'spm'
  const cadence = average(a.streams?.cadence ?? null)
  const temp = average(a.streams?.temp ?? null)
  const stats = [
    [t('activity.distance'), `${number.format(a.distanceM / 1000)} km`],
    [t('activity.time'), formatDuration(a.durationS)],
    !mixed && [
      t(isCyclingSport(a.sport) ? 'activity.speed' : 'activity.pace'),
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

  const effortTable = hasEfforts && (
    <table className="w-full text-sm tabular-nums">
      <thead className="text-left text-xs text-muted-foreground">
        <tr>
          <th className="py-1 font-normal">{t('records.distance')}</th>
          <th className="py-1 font-normal">{t('records.time')}</th>
          <th className="py-1 font-normal">{t('activity.pace')}</th>
          <th className="py-1 font-normal">
            <span className="sr-only">{t('records.title')}</span>
          </th>
        </tr>
      </thead>
      <tbody className="divide-y">
        {bestEfforts.data!.map((e) => {
          const yearBest = holds(yearBests.data, e)
          return (
            <tr key={e.distanceM}>
              <td className="py-1.5">{t(`records.distances.${e.distanceM as RecordDistance}`)}</td>
              <td className="py-1.5">{formatDuration(Math.round(e.elapsedS))}</td>
              <td className="py-1.5">{formatPace(e.elapsedS, recordMeters(e.distanceM))}</td>
              <td className="py-1.5 text-right">
                {e.recordStatus ? (
                  <RecordAward status={e.recordStatus} />
                ) : (
                  yearBest &&
                  recordYear !== undefined && <RecordAward status="year" year={recordYear} />
                )}
              </td>
            </tr>
          )
        })}
      </tbody>
    </table>
  )

  return (
    <section>
      {original.userId === me?.id ? (
        <BackLink />
      ) : visitor ? (
        <Link
          to="/app/runners/$runnerId"
          params={{ runnerId: original.userId }}
          className="text-sm text-muted-foreground underline underline-offset-2"
        >
          ← {original.displayName}
        </Link>
      ) : (
        <BackLink fallback="feed" />
      )}
      <p className="mt-4 text-sm text-muted-foreground">{when.format(new Date(a.startedAt))}</p>
      <div className="mt-1 flex flex-wrap items-center justify-between gap-3">
        <Heading level={1} className="flex min-w-0 items-center gap-3">
          <span className="wrap-anywhere" style={{ viewTransitionName: `activity-title-${a.id}` }}>
            {activityTitle(original, t)}
          </span>
          <SportIcon
            sport={a.sport}
            className="size-9 shrink-0"
            style={{ viewTransitionName: `activity-sport-${a.id}` }}
          />
        </Heading>
        {original.userId === me?.id && (
          <Button asChild size="sm" variant="secondary-outline" className="ml-auto">
            <Link
              to="/app/activities/$activityId/edit"
              params={{ activityId: a.id }}
              search={search}
            >
              {t('activity.edit')}
            </Link>
          </Button>
        )}
      </div>
      {(a.attribution ?? a.device) && (
        <p className="mt-1 text-xs text-muted-foreground">{a.attribution ?? a.device}</p>
      )}
      {original.userId !== me?.id && (
        <p className="mt-3 text-sm text-muted-foreground">
          {t('social.sharedDetail', { name: original.displayName })}
        </p>
      )}

      {a.description && (
        <p className="mt-4 whitespace-pre-wrap text-body wrap-anywhere">{a.description}</p>
      )}
      {original.userId === me?.id && original.excludeFromRecords && (
        <p className="mt-4 text-sm text-muted-foreground">{t('activity.excludedFromRecords')}</p>
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
          <p className="text-metadata text-muted-foreground">{t('equipment.title')}</p>
          <SlowLoading loading={equipment.isPending}>
            <ul className="mt-2 flex flex-wrap gap-2">
              {equipment.data
                ?.filter((item) => a.equipmentIds.includes(item.id))
                .map((item) => (
                  <li key={item.id} className="rounded-full bg-muted px-2 py-0.5 text-xs">
                    {item.name}
                  </li>
                ))}
            </ul>
            {equipment.error && (
              <p role="alert" className="text-sm text-destructive">
                <ErrorMessage error={equipment.error} />
              </p>
            )}
          </SlowLoading>
        </div>
      )}
      {a.tags.length > 0 && (
        <div className="mt-5">
          <p className="text-metadata text-muted-foreground">{t('tags.label')}</p>
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

      {ownEfforts && bestEfforts.data?.some((e) => e.recordStatus) && (
        <section className={card({ className: 'mt-6' })} aria-labelledby="activity-records-title">
          <Heading level={2} id="activity-records-title">
            {t('records.activityRecords')}
          </Heading>
          {sourceNote}
          <ul className="mt-3 grid gap-3 sm:grid-cols-2">
            {bestEfforts.data
              .filter((e) => e.recordStatus)
              .map((e) => (
                <li
                  key={e.distanceM}
                  className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 rounded-lg bg-muted/50 px-3 py-2"
                >
                  <span className="text-sm font-medium">
                    {t(`records.distances.${e.distanceM as RecordDistance}`)}
                    <span className="ml-3 tabular-nums">{formatDuration(e.elapsedS)}</span>
                  </span>
                  <RecordAward status={e.recordStatus!} />
                </li>
              ))}
          </ul>
        </section>
      )}
      {original.userId === me?.id &&
        a.streams === null &&
        sportIcons[knownSport(a.sport)] === 'run' && (
          <p className="mt-4 text-sm text-muted-foreground">{t('activity.noRecordsWithoutFile')}</p>
        )}
      {bestEfforts.error && (
        <p role="alert" className="mt-4 text-sm text-destructive">
          <ErrorMessage error={bestEfforts.error} />{' '}
          <Button size="sm" variant="secondary-outline" onClick={() => void bestEfforts.refetch()}>
            {t('activities.retry')}
          </Button>
        </p>
      )}

      {((!mixed && a.laps.length > 0) || (view && view.splits.length > 0) || hasEfforts) && (
        <div className="mt-8">
          <div className="flex flex-wrap gap-2">
            {!mixed && a.laps.length > 0 && (
              <Button
                id={`${lapsId}-toggle`}
                type="button"
                variant="secondary-outline"
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
                variant="secondary-outline"
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
            {hasEfforts && (
              <Button
                id={`${effortsId}-toggle`}
                type="button"
                variant="secondary-outline"
                aria-expanded={effortsOpen}
                aria-controls={effortsId}
                onClick={() => setOpenPanel((open) => (open === 'efforts' ? null : 'efforts'))}
              >
                {t('records.bestEfforts')}
                <ChevronDown
                  aria-hidden="true"
                  className={`transition-transform duration-250 motion-reduce:transition-none ${effortsOpen ? 'rotate-180' : ''}`}
                />
              </Button>
            )}
          </div>
          {openPanel && sourceNote}
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

          {hasEfforts && (
            <div
              id={effortsId}
              aria-hidden={!effortsOpen}
              inert={!effortsOpen}
              className={`grid transition-[grid-template-rows] duration-250 ease-out motion-reduce:transition-none ${effortsOpen ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]'}`}
            >
              <div className="min-h-0 overflow-hidden">
                <div
                  role="region"
                  aria-labelledby={`${effortsId}-toggle`}
                  tabIndex={effortsOpen ? 0 : -1}
                  className="mt-3 max-h-[min(16rem,25svh)] overflow-auto overscroll-contain rounded-lg border px-3 focus-visible:outline-2 focus-visible:outline-ring [&_thead]:sticky [&_thead]:top-0 [&_thead]:bg-background"
                >
                  {effortTable}
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
          <Heading level={2} className="mb-2">
            {t('activity.charts')}
          </Heading>
          {sourceNote}
          <ActivityCharts axis={view.axis} series={view.series} />
        </div>
      )}

      {view && view.splits.length > 0 && (
        <div className="mt-8">
          <Heading level={2} className="mb-2">
            {t('activity.splits')}
          </Heading>
          {sourceNote}
          {splitTable}
        </div>
      )}

      {hasEfforts && (
        <div className="mt-8">
          <Heading level={2} className="mb-2">
            {t('records.bestEfforts')}
          </Heading>
          {sourceNote}
          {effortTable}
        </div>
      )}

      {original.userId === me?.id && <DeleteActivity activityId={original.id} />}
    </section>
  )
}

// Last on the page: deleting is rare and permanent, so it does not compete with editing.
function DeleteActivity({ activityId }: { activityId: string }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const { returnTo, year, tag, sport } = useSearch({ from: '/app/activities/$activityId' })
  const [confirming, setConfirming] = useState(false)
  const remove = useMutation({
    mutationFn: () => api.send('DELETE', `/api/activities/${activityId}`),
    onSuccess: async () => {
      setConfirming(false)
      if (returnTo) await navigate({ href: returnTo })
      else await navigate({ to: '/app/activities', search: { year, tag, sport } })
      // After leaving: refetching the deleted activity on this page would only show "not found"
      await refreshActivityQueries(queryClient)
    },
  })
  return (
    <div className="mt-10 border-t pt-6">
      <Button
        variant="destructive-outline"
        onClick={() => {
          remove.reset()
          setConfirming(true)
        }}
      >
        {t('activity.delete')}
      </Button>
      <ConfirmationModal
        open={confirming}
        title={t('activities.confirmDelete')}
        description={t('activities.deleteWarning')}
        confirmLabel={t('activities.delete')}
        cancelLabel={t('common.cancel')}
        pending={remove.isPending}
        error={remove.error}
        onConfirm={() => remove.mutate()}
        onClose={() => setConfirming(false)}
      />
    </div>
  )
}
