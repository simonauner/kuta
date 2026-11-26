import { Stat } from '@/components/stat'
import { SlowLoading } from '@/components/slow-loading'
import { isCyclingSport, isGymSport, isRunningSport } from '@/lib/sports'
import { SportIcon } from '@/components/sport-icon'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useId, useMemo, useState } from 'react'
import { ArrowDown, ChevronDown } from 'lucide-react'
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
import { ErrorMessage, ErrorText } from '@/components/error-message'
import { QueryError } from '@/components/query-error'
import { RecordAward } from '@/components/record-award'
import { ActivityAnalysis } from '@/components/activity-analysis'
import { HeartRateZones } from '@/components/heart-rate-zones'
import { ApiError } from '@/lib/api'
import { activityQuery, refreshActivityQueries } from '@/lib/activity-query'
import { api } from '@/lib/api'
import {
  formatImprovement,
  recordMeters,
  type RecordDistance,
  utcYear,
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
  coloredRoute,
  routeRamp,
} from '@/lib/activity'
import { card } from '@/components/ui/card-variants'
import { VisitorNotice } from '@/components/visitor-notice'
import { ConfirmationModal } from '@/components/ui/confirmation-modal'
import { SimilarRoutes } from '@/components/similar-routes'
import { Companions } from '@/components/companions'
import { OrienteeringLegs, SameCourse } from '@/components/orienteering'
import { courseStats } from '@/lib/orienteering'

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

type RouteMetric = 'pace' | 'hr' | 'alt'

function RouteColoring({
  value,
  options,
  onChange,
  pace,
}: {
  value: 'route' | RouteMetric
  options: RouteMetric[]
  onChange: (value: 'route' | RouteMetric) => void
  pace: boolean
}) {
  const { t } = useTranslation()
  const labels = {
    route: t('activity.routeColoringNone'),
    pace: pace ? t('activity.pace') : t('activity.speed'),
    hr: t('activity.routeColoringHr'),
    alt: t('activity.routeColoringAlt'),
  }
  return (
    <div
      role="group"
      aria-label={t('activity.routeColoring')}
      className="inline-flex rounded-lg bg-muted p-1"
    >
      {(['route', ...options] as const).map((option) => (
        <Button
          key={option}
          variant="segment"
          size="sm"
          aria-pressed={value === option}
          onClick={() => onChange(option)}
        >
          {labels[option]}
        </Button>
      ))}
    </div>
  )
}

// Relative to this activity: the ends are its 5th and 95th percentile (coloredRoute)
function RouteLegend({ low, high }: { low: string; high: string }) {
  return (
    <div className="flex items-center gap-2 text-metadata text-muted-foreground">
      <span>{low}</span>
      <span aria-hidden="true" className="flex h-2 w-32 overflow-hidden rounded-full">
        {routeRamp.map((color) => (
          <span key={color} className="flex-1" style={{ backgroundColor: color }} />
        ))}
      </span>
      <span>{high}</span>
    </div>
  )
}

export function ActivityPage() {
  const { activityId } = useParams({ from: '/app/activities/$activityId' })
  const { t, i18n } = useTranslation()
  const search = useSearch({ from: '/app/activities/$activityId' })
  const { data: me } = useQuery(meQuery)
  const visitor = me === null
  const equipment = useQuery({ ...equipmentQuery, enabled: !visitor })
  const [openPanel, setOpenPanel] = useState<'laps' | 'splits' | 'efforts' | null>(null)
  // An enlarged or fullscreen map shows the tables in its side panel instead of above it
  const [tablesBeside, setTablesBeside] = useState(false)
  // The map frame carries MapLibre's fullscreen class in fullscreen, where the panel gets the whole screen height
  const tableHeight = tablesBeside
    ? 'max-h-[calc(80svh-9rem)] [.maplibregl-pseudo-fullscreen_&]:max-h-[calc(100svh-9rem)]'
    : 'max-h-[min(16rem,25svh)]'
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
  const hasEfforts = !!bestEfforts.data?.length
  // Places of all time and in the year (only the owner gets a status), as the feed card highlights.
  const awarded = bestEfforts.data?.filter((e) => e.recordStatus) ?? []
  const [selection, setSelection] = useState<{ id: string; part: ActivityPart | null } | null>(null)
  const selectedPart = selection?.id === activityId ? selection.part : null
  const selectedSport = selectedPart?.sport ?? activity.data?.sport
  const mixed = (activity.data?.parts.length ?? 0) > 0 && !selectedPart
  // Laps, elevation and a missing route say little about a gym session
  const gym = isGymSport(selectedSport ?? '')
  const [routeColoring, setRouteColoring] = useState<'route' | RouteMetric>('route')

  const view = useMemo(() => {
    const original = activity.data?.streams
    const streams = original && selectedPart ? partStreams(original, selectedPart) : original
    if (!streams) return null
    const segments = routeSegments(streams)
    const coordinates = segments.flat()
    const series: ChartSeries[] = []
    if (streams.alt && !gym)
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
    // Values to color the route by, higher = warmer; pace uses the smoothed speed so it matches the chart
    const minSpeed = selectedSport === 'swimming' ? 0.05 : 0.5
    const metrics: Partial<Record<RouteMetric, (number | null)[]>> = mixed
      ? {}
      : {
          pace: streams.speed
            ? paceSeries(streams.speed, minSpeed).map((v) => (v === null ? null : 1000 / v))
            : undefined,
          hr: streams.hr ?? undefined,
          alt: streams.alt ?? undefined,
        }
    return {
      coordinates,
      routes,
      metrics,
      streams,
      series,
      axis: chartAxis(streams),
      splits: mixed ? [] : splits(streams),
    }
  }, [activity.data, selectedPart, selectedSport, mixed, gym, t])

  const metric = routeColoring !== 'route' && view?.metrics[routeColoring] ? routeColoring : null
  const colored = useMemo(
    () => (metric && view ? coloredRoute(view.streams, view.metrics[metric]!) : null),
    [metric, view],
  )
  const formatMetric = (m: RouteMetric, v: number) =>
    m === 'pace'
      ? formatSportPace(selectedSport ?? '', 1, v, i18n.resolvedLanguage)
      : `${Math.round(v)} ${m === 'hr' ? 'bpm' : 'm'}`

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
        aerobicTrainingEffect: null,
        anaerobicTrainingEffect: null,
        calories: null,
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
  // Garmin's wording for each whole step of the 0–5 scale; 5.0 is the top step
  const trainingEffect = (value: number) =>
    `${value.toLocaleString(i18n.resolvedLanguage, { minimumFractionDigits: 1, maximumFractionDigits: 1 })} ${t(
      (
        [
          'activity.trainingEffect.none',
          'activity.trainingEffect.minor',
          'activity.trainingEffect.maintaining',
          'activity.trainingEffect.improving',
          'activity.trainingEffect.highlyImproving',
          'activity.trainingEffect.overreaching',
        ] as const
      )[Math.min(5, Math.floor(value))],
    )}`
  // Grade-adjusted pace for running: the activity's pace scaled by the splits' grade-adjusted share of distance.
  const gapSplits =
    isRunningSport(a.sport) && view?.splits.every((s) => s.adjustedM !== null) ? view.splits : []
  const adjustedShare =
    gapSplits.reduce((sum, s) => sum + s.adjustedM!, 0) /
    gapSplits.reduce((sum, s) => sum + s.distanceM, 0)
  const orienteering = !mixed && a.sport === 'orienteering'
  const course =
    orienteering && a.courseLengthM !== null
      ? courseStats(a.durationS, a.distanceM, a.courseLengthM)
      : null
  // Distance, pace and elevation say nothing about a gym session
  const stats = [
    !gym && [t('activity.distance'), `${number.format(a.distanceM / 1000)} km`],
    [t('activity.time'), formatDuration(a.durationS)],
    !mixed &&
      !gym && [
        t(isCyclingSport(a.sport) ? 'activity.speed' : 'activity.pace'),
        formatSportPace(a.sport, a.movingS ?? a.durationS, a.distanceM, i18n.resolvedLanguage),
      ],
    !mixed &&
      gapSplits.length > 0 && [
        t('activity.gap'),
        formatSportPace(
          a.sport,
          a.movingS ?? a.durationS,
          a.distanceM * adjustedShare,
          i18n.resolvedLanguage,
        ),
      ],
    course && [t('orienteering.kmTime'), `${formatDuration(course.kmTimeS)} /km`],
    course && [
      t('orienteering.courseOverdistance'),
      new Intl.NumberFormat(i18n.resolvedLanguage, {
        style: 'percent',
        signDisplay: 'always',
        maximumFractionDigits: 0,
      }).format(course.overdistance),
    ],
    !gym &&
      a.elevationGainM !== null && [t('activity.elevation'), `${Math.round(a.elevationGainM)} m`],
    a.movingS !== null && [t('activity.movingTime'), formatDuration(a.movingS)],
    a.avgHr !== null && [t('activity.avgHr'), `${a.avgHr} bpm`],
    a.maxHr !== null && [t('activity.maxHr'), `${a.maxHr} bpm`],
    a.calories !== null && [
      t('activity.calories'),
      `${new Intl.NumberFormat(i18n.resolvedLanguage).format(a.calories)} kcal`,
    ],
    !mixed && cadence !== null && [t('activity.avgCadence'), `${cadence} ${cadenceUnit}`],
    temp !== null && [t('activity.avgTemp'), `${temp} °C`],
    a.aerobicTrainingEffect !== null && [
      t('activity.trainingEffect.aerobic'),
      trainingEffect(a.aerobicTrainingEffect),
    ],
    a.anaerobicTrainingEffect !== null && [
      t('activity.trainingEffect.anaerobic'),
      trainingEffect(a.anaerobicTrainingEffect),
    ],
  ].filter((s): s is [string, string] => !!s)

  const splitTable = view && (
    <table className="w-full text-sm tabular-nums">
      <thead className="text-left text-xs text-muted-foreground">
        <tr>
          <th className="py-1 font-normal">{t('activity.splitKm')}</th>
          <th className="py-1 font-normal">{t('activity.splitPace')}</th>
          {gapSplits.length > 0 && (
            <th className="py-1 font-normal">
              <abbr title={t('activity.gap')} className="no-underline">
                {t('activity.splitGap')}
              </abbr>
            </th>
          )}
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
            {gapSplits.length > 0 && (
              <td className="py-1.5">
                {formatSportPace(a.sport, s.durationS, s.adjustedM!, i18n.resolvedLanguage)}
              </td>
            )}
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
          return (
            <tr key={e.distanceM}>
              <td className="py-1.5">{t(`records.distances.${e.distanceM as RecordDistance}`)}</td>
              <td className="py-1.5">{formatDuration(Math.round(e.elapsedS))}</td>
              <td className="py-1.5">{formatPace(e.elapsedS, recordMeters(e.distanceM))}</td>
              <td className="py-1.5 text-right">
                {e.recordStatus && <RecordAward status={e.recordStatus} year={utcYear(e)} />}
              </td>
            </tr>
          )
        })}
      </tbody>
    </table>
  )

  const tablePanels = ((!mixed && !gym && a.laps.length > 0) ||
    (view && view.splits.length > 0) ||
    hasEfforts) && (
    <div>
      <div className="flex flex-wrap gap-2">
        {!mixed && !gym && a.laps.length > 0 && (
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
      {!mixed && !gym && a.laps.length > 0 && (
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
              className={`mt-3 ${tableHeight} overflow-auto overscroll-contain rounded-lg border px-3 focus-visible:outline-2 focus-visible:outline-ring`}
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
              className={`mt-3 ${tableHeight} overflow-auto overscroll-contain rounded-lg border px-3 focus-visible:outline-2 focus-visible:outline-ring [&_thead]:sticky [&_thead]:top-0 [&_thead]:bg-background`}
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
              className={`mt-3 ${tableHeight} overflow-auto overscroll-contain rounded-lg border px-3 focus-visible:outline-2 focus-visible:outline-ring [&_thead]:sticky [&_thead]:top-0 [&_thead]:bg-background`}
            >
              {effortTable}
            </div>
          </div>
        </div>
      )}
    </div>
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
          {/* The activity's own sport, also while a part is selected */}
          <SportIcon
            sport={original.sport}
            className="size-9 shrink-0"
            style={{ viewTransitionName: `activity-sport-${a.id}` }}
          />
        </Heading>
        {original.userId === me?.id && (
          <Button
            size="sm"
            variant="secondary-outline"
            className="ml-auto"
            render={
              <Link
                to="/app/activities/$activityId/edit"
                params={{ activityId: a.id }}
                search={search}
              />
            }
          >
            {t('activity.edit')}
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
      {!visitor && <Companions activityId={original.id} own={original.userId === me?.id} />}
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
            <Stat
              key={sport}
              label={t(sportNameKey(sport))}
              value={`${number.format(
                original.parts
                  .filter((p) => p.sport === sport)
                  .reduce((sum, p) => sum + p.distanceM, 0) / 1000,
              )} km`}
            />
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
            <ErrorText error={equipment.error} />
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
          <Stat key={label} label={label} value={value} size={i < 3 ? 'xl' : 'sm'} />
        ))}
      </dl>

      {awarded.length > 0 && (
        <section className={card({ className: 'mt-6' })} aria-labelledby="activity-records-title">
          <Heading level={2} id="activity-records-title">
            {t('records.activityRecords')}
          </Heading>
          {sourceNote}
          <ul className="mt-3 grid gap-3 sm:grid-cols-2">
            {awarded.map((e) => (
              <li
                key={e.distanceM}
                className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 rounded-lg bg-muted/50 px-3 py-2"
              >
                <span className="inline-flex items-center text-sm font-medium">
                  {t(`records.distances.${e.distanceM as RecordDistance}`)}
                  <span className="ml-3 tabular-nums">{formatDuration(e.elapsedS)}</span>
                  {e.improvementS !== null && (
                    <span className="ml-3 inline-flex items-center gap-0.5 font-normal text-muted-foreground tabular-nums">
                      <ArrowDown className="size-3.5" aria-hidden="true" />
                      <span aria-hidden="true">{formatImprovement(e.improvementS)}</span>
                      <span className="sr-only">
                        {t('records.improvedBy', { time: formatImprovement(e.improvementS) })}
                      </span>
                    </span>
                  )}
                </span>
                <RecordAward status={e.recordStatus!} year={utcYear(e)} />
              </li>
            ))}
          </ul>
        </section>
      )}
      {!mixed && isRunningSport(a.sport) && (
        <ActivityAnalysis streams={a.streams} laps={a.laps} sourceNote={sourceNote} />
      )}
      {orienteering && (
        <>
          {original.userId === me?.id && a.courseLengthM === null && (
            <p className="mt-4 text-sm text-muted-foreground">{t('orienteering.noCourseLength')}</p>
          )}
          <OrienteeringLegs activity={a} own={original.userId === me?.id} />
          {!visitor && me && <SameCourse activity={original} meId={me.id} />}
        </>
      )}
      <HeartRateZones streams={a.streams} bounds={a.hrZones} sourceNote={sourceNote} />
      {original.userId === me?.id && a.streams === null && isRunningSport(a.sport) && (
        <p className="mt-4 text-sm text-muted-foreground">{t('activity.noRecordsWithoutFile')}</p>
      )}
      <QueryError query={bestEfforts} className="mt-4" />

      {!tablesBeside && tablePanels && <div className="mt-8">{tablePanels}</div>}

      <div className="mt-8">
        {view && view.coordinates.length > 1 ? (
          <>
            <ActivityMap
              coordinates={view.coordinates}
              routes={colored?.routes ?? view.routes}
              label={t('activity.map')}
              expandable
              onSideChange={(shown) => {
                setTablesBeside(shown)
                // Open the first table so the side panel isn't empty
                if (shown)
                  setOpenPanel(
                    (open) =>
                      open ??
                      (!mixed && !gym && a.laps.length > 0
                        ? 'laps'
                        : view.splits.length > 0
                          ? 'splits'
                          : hasEfforts
                            ? 'efforts'
                            : null),
                  )
              }}
              side={tablePanels || undefined}
            />
            {Object.values(view.metrics).some(Boolean) && (
              <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-2">
                <RouteColoring
                  value={metric ?? 'route'}
                  options={(['pace', 'hr', 'alt'] as const).filter((m) => view.metrics[m])}
                  onChange={setRouteColoring}
                  pace={!isCyclingSport(selectedSport ?? '')}
                />
                {metric && colored && (
                  <RouteLegend
                    low={formatMetric(metric, colored.min)}
                    high={formatMetric(metric, colored.max)}
                  />
                )}
              </div>
            )}
          </>
        ) : (
          !gym && <p className="text-sm text-muted-foreground">{t('activity.noRoute')}</p>
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

      {original.userId === me?.id && <SimilarRoutes activityId={original.id} />}

      {original.userId === me?.id && <DeleteActivity activityId={original.id} />}
    </section>
  )
}

// Last on the page: deleting is rare and permanent, so it does not compete with editing.
function DeleteActivity({ activityId }: { activityId: string }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const { returnTo, ...filters } = useSearch({ from: '/app/activities/$activityId' })
  const [confirming, setConfirming] = useState(false)
  const remove = useMutation({
    mutationFn: () => api.send('DELETE', `/api/activities/${activityId}`),
    onSuccess: async () => {
      setConfirming(false)
      if (returnTo) await navigate({ href: returnTo })
      else await navigate({ to: '/app/activities', search: filters })
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
