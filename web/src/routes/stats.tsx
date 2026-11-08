import { GarminDerivedNote } from '@/components/garmin'
import { FormField } from '@/components/ui/form-field'
import { Heading } from '@/components/ui/heading'
import { useEffect, useState } from 'react'
import { LoaderCircle, Maximize2, Minimize2, Sparkles } from 'lucide-react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { QueryError } from '@/components/query-error'
import { PredictionsSection, RecordsSection } from '@/components/records'
import { ActivitiesEmptyState } from '@/components/activities-empty-state'
import { SlowLoading } from '@/components/slow-loading'
import { TrainingLoadSection } from '@/components/training-load'
import { AerobicEfficiencySection } from '@/components/aerobic-efficiency'
import { YearComparison } from '@/components/year-comparison'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import { formatDuration } from '@/lib/activity'
import { scheduleSlowLoading } from '@/lib/slow-loading'
import { sportGroupsQuery, activityYearsQuery } from '@/lib/activity-query'
import { sportGroupLabel } from '@/lib/sport-groups'
import { card } from '@/components/ui/card-variants'
import { Select } from '@/components/ui/select'
import { HelpPopover } from '@/components/help-popover'
import {
  change,
  axisTicks,
  lineRuns,
  rollingPeriods,
  statisticsRanges,
  type StatisticsPeriod,
  type StatisticsRange,
} from '@/lib/statistics-period'

type StatisticsBucket = {
  start: string
  count: number
  durationS: number
  distanceM: number
  paceSPerKm: number | null
  avgHr: number | null
}
type StatisticsResult = { buckets: StatisticsBucket[]; total: StatisticsBucket }

const metrics = ['distanceM', 'durationS', 'paceSPerKm', 'avgHr'] as const
type Metric = (typeof metrics)[number]

// Overview follows the chosen year; Analysis shows current form, which no year affects.
function StatsTabs() {
  const { t } = useTranslation()
  const tabClass =
    'rounded-md px-3 py-1.5 text-sm font-medium text-muted-foreground transition-colors hover:text-foreground'
  const activeProps = { className: 'bg-background text-foreground shadow-sm' }
  return (
    <nav aria-label={t('stats.views')} className="mt-4 inline-flex rounded-lg bg-muted p-1">
      <Link
        to="/app/stats"
        activeOptions={{ exact: true }}
        className={tabClass}
        activeProps={activeProps}
      >
        {t('stats.overview')}
      </Link>
      <Link to="/app/stats/analysis" className={tabClass} activeProps={activeProps}>
        {t('stats.analysis')}
      </Link>
    </nav>
  )
}

// The running year and, while it's fresh, the one before it
function YearReviewLinks({
  years,
  currentYear,
  currentMonth,
}: {
  years: number[]
  currentYear: number
  currentMonth: number
}) {
  const { t } = useTranslation()
  const shown = years
    .filter((y) => y === currentYear || (y === currentYear - 1 && currentMonth <= 3))
    .sort((a, b) => b - a)
  if (shown.length === 0) return null
  return (
    <div className="mt-4 flex flex-wrap gap-2">
      {shown.map((year) => (
        <Button key={year} asChild variant="secondary-outline" size="sm">
          <Link to="/app/stats/year/$year" params={{ year: String(year) }}>
            <Sparkles aria-hidden="true" />
            {t(year === currentYear ? 'yearReview.linkSoFar' : 'yearReview.link', { year })}
          </Link>
        </Button>
      ))}
    </div>
  )
}

export function AnalysisPage() {
  const { t } = useTranslation()
  return (
    <section>
      <Heading level={1}>{t('nav.stats')}</Heading>
      <StatsTabs />
      <p className="mt-4 text-muted-foreground">{t('stats.analysisDescription')}</p>
      <GarminDerivedNote className="mt-1" />
      <TrainingLoadSection />
      <AerobicEfficiencySection />
      <PredictionsSection />
    </section>
  )
}

// Dates from the API are local calendar dates; parse them as UTC midnight and format in UTC so no time zone shifts the day.
const parseDate = (text: string) => new Date(`${text}T00:00:00Z`)
const addDays = (text: string, days: number) =>
  new Date(parseDate(text).getTime() + days * 86_400_000)

// The period and the one before it load together, so the shown numbers and their change always belong to the same period.
function statisticsQuery(
  timeZone: string,
  sportGroup: string,
  ranges: { current: StatisticsRange; previous: StatisticsRange },
  enabled: boolean,
) {
  const read = (range: StatisticsRange) => {
    const params = new URLSearchParams({ timeZone, ...range })
    if (sportGroup) params.set('sportGroup', sportGroup)
    return api.get<StatisticsResult>(`/api/activities/statistics?${params}`)
  }
  return {
    enabled,
    queryKey: ['activities', 'statistics', timeZone, ranges, sportGroup],
    queryFn: async () => {
      const [current, previous] = await Promise.all([read(ranges.current), read(ranges.previous)])
      return { ranges, current, previous }
    },
    // Keep showing the previous period while another loads.
    placeholderData: keepPreviousData,
  }
}

export function StatsPage() {
  const { t, i18n } = useTranslation()
  const [today] = useState(() => {
    const now = new Date()
    return { year: now.getFullYear(), month: now.getMonth() + 1, day: now.getDate() }
  })
  const currentYear = today.year
  const [period, setPeriod] = useState<StatisticsPeriod>('12w')
  const year = typeof period === 'number' ? period : currentYear
  // Sport group: running also counts trail and virtual running, etc. Empty = all.
  const [sportGroup, setSportGroup] = useState('running')
  const sportGroups = useQuery(sportGroupsQuery)
  const [metric, setMetric] = useState<Metric>('distanceM')
  const [large, setLarge] = useState(false)
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const years = useQuery(activityYearsQuery(timeZone))
  const hasNoActivities = years.isSuccess && years.data.length === 0
  const enabled = !years.isPending && !hasNoActivities
  const statistics = useQuery(
    statisticsQuery(timeZone, sportGroup, statisticsRanges(period, today), enabled),
  )
  // Formatting follows the shown data, which is the previous period until the new one has loaded.
  const ranges = statistics.data?.ranges ?? statisticsRanges(period, today)
  const { bucket } = ranges.current
  const buckets = statistics.data?.current.buckets ?? []
  const total = statistics.data?.current.total
  const [slowSince, setSlowSince] = useState<number | null>(null)
  const switching = statistics.isPlaceholderData
  useEffect(() => scheduleSlowLoading(switching, slowSince, setSlowSince), [switching, slowSince])
  const availableYears = [...new Set([currentYear, year, ...(years.data ?? [])])].sort(
    (a, b) => b - a,
  )
  const number = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 1 })
  const percent = new Intl.NumberFormat(i18n.resolvedLanguage, {
    maximumFractionDigits: 0,
    signDisplay: 'exceptZero',
  })
  const bucketFormat = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    timeZone: 'UTC',
    ...(bucket === 'week' ? { day: 'numeric', month: 'short' } : { month: 'short' }),
  })
  const fullBucketFormat = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    timeZone: 'UTC',
    ...(bucket === 'week' ? { dateStyle: 'medium' } : { month: 'long', year: 'numeric' }),
  })
  const rangeFormat = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    timeZone: 'UTC',
    dateStyle: 'medium',
  })
  // Shown with the inclusive last day, since the API's end date is exclusive.
  const formatRange = (range: StatisticsRange) =>
    rangeFormat.formatRange(parseDate(range.from), addDays(range.to, -1))
  // The last week or month is still running when the period ends today.
  const endsToday = ranges.current.to === statisticsRanges('4w', today).current.to
  const bucketLabel = (b: StatisticsBucket, index: number) =>
    endsToday && index === buckets.length - 1
      ? `${bucketFormat.format(parseDate(b.start))} (${t('stats.inProgress')})`
      : bucketFormat.format(parseDate(b.start))
  const count = total?.count ?? 0
  const changeText = (key: 'count' | 'distanceM' | 'durationS') => {
    if (!total || !statistics.data) return null
    const value = change(total[key], statistics.data.previous.total[key])
    return value === null ? null : `${percent.format(Math.round(value))} %`
  }
  const step = (n: number) => Math.max(1, Math.ceil(buckets.length / n))
  // The enlarged chart is wider and taller, with more gridlines and labels.
  const [width, bottom] = large ? [1000, 380] : [600, 180]
  // The plot leaves room on the right for the gridline labels.
  const plotWidth = width - 80
  const ticks = axisTicks(
    metric,
    buckets.map((m) => m[metric]),
    large ? 8 : 4,
  )
  const [lower, upper] = [ticks[0], ticks[ticks.length - 1]]
  const zeroBased = metric === 'distanceM' || metric === 'durationS'
  // A faster pace is drawn higher, as runners read it.
  const valueY = (value: number) => {
    const share = (value - lower) / (upper - lower)
    return metric === 'paceSPerKm' ? 20 + share * (bottom - 20) : bottom - share * (bottom - 20)
  }
  const pointX = (index: number) => ((index + 0.5) * plotWidth) / buckets.length
  const pointY = (index: number) => valueY(buckets[index][metric] ?? lower)
  const tickLabel = (value: number) => {
    if (metric === 'distanceM') return `${number.format(value / 1000)} km`
    if (metric === 'durationS') return `${number.format(value / 3600)} h`
    if (metric === 'paceSPerKm') return formatDuration(value)
    return number.format(value)
  }
  const format = (value: number | null, key: Metric) => {
    if (value === null) return '–'
    if (key === 'distanceM') return `${number.format(value / 1000)} km`
    if (key === 'durationS') return formatDuration(value)
    if (key === 'paceSPerKm') return `${formatDuration(Math.round(value))} /km`
    return `${number.format(value)} bpm`
  }

  if (years.isPending || hasNoActivities) {
    return (
      <section>
        <Heading level={1}>{t('nav.stats')}</Heading>
        <StatsTabs />
        <p className="mt-4 text-muted-foreground">{t('stats.description')}</p>
        <SlowLoading loading={years.isPending}>
          <ActivitiesEmptyState context="stats" />
        </SlowLoading>
      </section>
    )
  }

  return (
    <section>
      <Heading level={1}>{t('nav.stats')}</Heading>
      <StatsTabs />
      <p className="mt-4 text-muted-foreground">{t('stats.description')}</p>
      <GarminDerivedNote className="mt-1" />
      <YearReviewLinks
        years={years.data ?? []}
        currentYear={currentYear}
        currentMonth={today.month}
      />
      <div className="mt-6 flex flex-wrap gap-4">
        <FormField label={t('stats.period')} className="text-sm font-medium">
          {(id) => (
            <Select
              id={id}
              className="w-fit"
              value={period}
              onChange={(e) => {
                const value = e.target.value
                setPeriod(/^\d+$/.test(value) ? Number(value) : (value as StatisticsPeriod))
              }}
            >
              {rollingPeriods.map((value) => (
                <option key={value} value={value}>
                  {t(`stats.periods.${value}`)}
                </option>
              ))}
              {availableYears.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </Select>
          )}
        </FormField>
        <FormField label={t('newActivity.sport')} className="text-sm font-medium">
          {(id) => (
            <Select
              id={id}
              className="w-fit"
              value={sportGroup}
              onChange={(e) => setSportGroup(e.target.value)}
            >
              <option value="">{t('stats.allSports')}</option>
              {(sportGroups.data ?? (sportGroup ? [{ key: sportGroup }] : [])).map(({ key }) => (
                <option key={key} value={key}>
                  {sportGroupLabel(key, t)}
                </option>
              ))}
            </Select>
          )}
        </FormField>
      </div>
      {sportGroups.error && (
        <p role="alert" className="mt-4 text-sm text-destructive">
          <ErrorMessage error={sportGroups.error} />
        </p>
      )}
      <QueryError query={years} className="mt-4" />
      <div className="relative" aria-busy={switching}>
        {slowSince !== null && (
          <p role="status" className="absolute inset-x-0 top-8 z-10 flex justify-center">
            <span className="flex items-center gap-2 rounded-full border bg-background px-3 py-1.5 text-sm text-muted-foreground shadow-sm">
              <LoaderCircle aria-hidden="true" className="size-4 motion-safe:animate-spin" />
              {t('slowLoading')}
            </span>
          </p>
        )}
        <SlowLoading loading={statistics.isPending}>
          <QueryError query={statistics} className="mt-4" />
          {statistics.isSuccess &&
            (count === 0 ? (
              <p className="mt-8 text-muted-foreground motion-safe:animate-fade-in-up">
                {t('stats.empty')}
              </p>
            ) : (
              <div
                className={`motion-safe:animate-fade-in-up transition-opacity ${slowSince !== null ? 'opacity-50' : ''}`}
              >
                <p className="mt-6 text-metadata text-muted-foreground">
                  {formatRange(ranges.current)}
                </p>
                <dl className="mt-2 grid grid-cols-3 gap-3">
                  {(
                    [
                      ['count', number.format(count)],
                      ['distanceM', format(total?.distanceM ?? 0, 'distanceM')],
                      ['durationS', format(total?.durationS ?? 0, 'durationS')],
                    ] as const
                  ).map(([key, value]) => (
                    <div key={key} className={card({ padding: 'compact' })}>
                      <dt className="text-xs text-muted-foreground">{t(`stats.${key}`)}</dt>
                      <dd className="mt-2 break-words text-lg font-semibold tabular-nums sm:text-2xl">
                        {value}
                      </dd>
                      {changeText(key) && (
                        <dd className="mt-1 text-xs tabular-nums text-muted-foreground">
                          {changeText(key)}
                        </dd>
                      )}
                    </div>
                  ))}
                </dl>
                <p className="mt-2 text-metadata text-muted-foreground">
                  {t('stats.comparedWith', { range: formatRange(ranges.previous) })}
                </p>
                <div className="mt-8 flex items-center justify-between gap-4">
                  <Heading level={2}>
                    {t(bucket === 'week' ? 'stats.weekly' : 'stats.monthly')}
                  </Heading>
                  <HelpPopover label={t('stats.methodLabel')}>{t('stats.method')}</HelpPopover>
                </div>
                <div
                  className={card({
                    // Like the enlarged map: wider than the column, centred and kept inside the viewport.
                    className: `mt-4 transition-[margin] duration-300 motion-reduce:transition-none ${large ? 'lg:mx-[calc(50%-min(50vw-1rem,40rem))]' : ''}`,
                  })}
                >
                  <FormField
                    layout="inline"
                    label={t('yearComparison.metric')}
                    className="justify-between text-sm font-medium"
                  >
                    {(id) => (
                      <div className="flex items-center gap-2">
                        <Select
                          id={id}
                          className="w-fit"
                          value={metric}
                          onChange={(e) => setMetric(e.target.value as Metric)}
                        >
                          {metrics.map((key) => (
                            <option key={key} value={key}>
                              {t(`stats.${key}`)}
                            </option>
                          ))}
                        </Select>
                        {/* On a small screen the chart already fills the width. */}
                        <Button
                          type="button"
                          variant="secondary-outline"
                          size="icon"
                          className="hidden lg:inline-flex"
                          aria-label={t(large ? 'stats.chartSmaller' : 'stats.chartLarger')}
                          title={t(large ? 'stats.chartSmaller' : 'stats.chartLarger')}
                          aria-pressed={large}
                          onClick={() => setLarge((v) => !v)}
                        >
                          {large ? <Minimize2 /> : <Maximize2 />}
                        </Button>
                      </div>
                    )}
                  </FormField>
                  <p className="sr-only">{t('stats.tableNote')}</p>
                  <svg
                    viewBox={`0 0 ${width} ${bottom + 30}`}
                    className="mt-2 w-full text-primary"
                    role="img"
                    aria-label={`${t(bucket === 'week' ? 'stats.weekly' : 'stats.monthly')}: ${t(`stats.${metric}`)} · ${formatRange(ranges.current)}`}
                  >
                    {ticks.map((tick) => (
                      <g key={tick} className="text-muted-foreground">
                        <line
                          x1="0"
                          x2={plotWidth}
                          y1={valueY(tick)}
                          y2={valueY(tick)}
                          stroke="currentColor"
                          opacity="0.3"
                        />
                        <text
                          x={width}
                          y={valueY(tick) + 6}
                          textAnchor="end"
                          fill="currentColor"
                          className="text-[20px] sm:text-xs"
                        >
                          {tickLabel(tick)}
                        </text>
                      </g>
                    ))}
                    {lineRuns(buckets.map((m) => m[metric])).map((run, runIndex, runs) => {
                      const points = run.map((index) => `${pointX(index)},${pointY(index)}`)
                      // Only averages (pace, heart rate) can be missing: a dashed line without area bridges the gap.
                      const before = runs[runIndex - 1]?.at(-1)
                      return (
                        <g key={run[0]}>
                          {before !== undefined && (
                            <line
                              x1={pointX(before)}
                              y1={pointY(before)}
                              x2={pointX(run[0])}
                              y2={pointY(run[0])}
                              stroke="currentColor"
                              strokeWidth="2"
                              strokeDasharray="6 6"
                              opacity="0.6"
                            />
                          )}
                          {/* Area only under sums; under an average it would suggest an amount. */}
                          {zeroBased && (
                            <polygon
                              points={`${pointX(run[0])},${bottom} ${points.join(' ')} ${pointX(run[run.length - 1])},${bottom}`}
                              fill="currentColor"
                              opacity="0.15"
                            />
                          )}
                          <polyline
                            points={points.join(' ')}
                            fill="none"
                            stroke="currentColor"
                            strokeWidth="3"
                            strokeLinejoin="round"
                          />
                        </g>
                      )
                    })}
                    {buckets.map((m, index) => (
                      <g key={m.start}>
                        <title>{`${fullBucketFormat.format(parseDate(m.start))}: ${format(m[metric], metric)}`}</title>
                        {m[metric] !== null && (
                          <circle
                            cx={pointX(index)}
                            cy={pointY(index)}
                            r="4"
                            className="fill-card"
                            stroke="currentColor"
                            strokeWidth="2.5"
                          />
                        )}
                        {index % step(large ? 24 : 12) === 0 && (
                          <text
                            // The first label starts at the edge so narrow week slots don't clip it.
                            x={index ? pointX(index) : 0}
                            y={bottom + 22}
                            textAnchor={index ? 'middle' : 'start'}
                            fill="currentColor"
                            className={`text-[20px] text-muted-foreground sm:text-xs ${index % step(4) ? 'hidden sm:block' : ''}`}
                          >
                            {bucketFormat.format(parseDate(m.start))}
                          </text>
                        )}
                      </g>
                    ))}
                  </svg>
                  {metric === 'avgHr' && !buckets.some((m) => m.avgHr !== null) && (
                    <p className="text-sm text-muted-foreground">{t('stats.noHeartRate')}</p>
                  )}
                </div>
                <div className="mt-6 overflow-x-auto">
                  <table className="w-full text-right text-sm tabular-nums">
                    <caption className="pb-3 text-left font-medium">
                      {t(bucket === 'week' ? 'stats.weekly' : 'stats.monthly')} ·{' '}
                      {formatRange(ranges.current)}
                    </caption>
                    <thead className="text-xs text-muted-foreground">
                      <tr>
                        <th scope="col" className="py-3 pr-3 text-left">
                          {t(bucket === 'week' ? 'stats.week' : 'stats.month')}
                        </th>
                        <th scope="col" className="px-2">
                          {t('stats.count')}
                        </th>
                        {metrics.map((key) => (
                          <th scope="col" key={key} className="px-2 font-medium">
                            {t(`stats.${key}`)}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody className="divide-y">
                      {buckets.map((m, index) => (
                        <tr key={m.start}>
                          <th scope="row" className="py-3 pr-3 text-left font-medium">
                            {bucketLabel(m, index)}
                          </th>
                          <td className="px-2">{m.count}</td>
                          {metrics.map((key) => (
                            <td key={key} className="whitespace-nowrap px-2">
                              {format(m[key], key)}
                            </td>
                          ))}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            ))}
        </SlowLoading>
      </div>
      <YearComparison year={year} sportGroup={sportGroup} />
      <RecordsSection year={year} />
    </section>
  )
}
