import { GarminDerivedNote } from '@/components/garmin'
import { FormField } from '@/components/ui/form-field'
import { Heading } from '@/components/ui/heading'
import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { RecordsSection } from '@/components/records'
import { ActivitiesEmptyState } from '@/components/activities-empty-state'
import { SlowLoading } from '@/components/slow-loading'
import { TrainingLoadSection } from '@/components/training-load'
import { YearComparison } from '@/components/year-comparison'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import { formatDuration } from '@/lib/activity'
import { sportGroupsQuery, activityYearsQuery } from '@/lib/activity-query'
import { sportGroupLabel } from '@/lib/sport-groups'
import { card } from '@/components/ui/card-variants'
import { Select } from '@/components/ui/select'
import { HelpPopover } from '@/components/help-popover'

type StatisticsMonth = {
  month: number
  count: number
  durationS: number
  distanceM: number
  paceSPerKm: number | null
  avgHr: number | null
}

const metrics = ['distanceM', 'durationS', 'paceSPerKm', 'avgHr'] as const
type Metric = (typeof metrics)[number]

export function StatsPage() {
  const { t, i18n } = useTranslation()
  const [currentYear] = useState(() => new Date().getFullYear())
  const [year, setYear] = useState(currentYear)
  // Sport group: running also counts trail and virtual running, etc. Empty = all.
  const [sportGroup, setSportGroup] = useState('running')
  const sportGroups = useQuery(sportGroupsQuery)
  const [metric, setMetric] = useState<Metric>('distanceM')
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const years = useQuery(activityYearsQuery(timeZone))
  const hasNoActivities = years.isSuccess && years.data.length === 0
  const statistics = useQuery({
    enabled: !years.isPending && !hasNoActivities,
    queryKey: ['activities', 'statistics', timeZone, year, sportGroup],
    queryFn: () => {
      const params = new URLSearchParams({ timeZone, year: String(year) })
      if (sportGroup) params.set('sportGroup', sportGroup)
      return api.get<StatisticsMonth[]>(`/api/activities/statistics?${params}`)
    },
  })
  const months = statistics.data ?? []
  const availableYears = [...new Set([currentYear, year, ...(years.data ?? [])])].sort(
    (a, b) => b - a,
  )
  const number = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 1 })
  const monthFormat = new Intl.DateTimeFormat(i18n.resolvedLanguage, { month: 'short' })
  const monthLabel = (month: number) => monthFormat.format(new Date(2000, month - 1, 1))
  const count = months.reduce((sum, m) => sum + m.count, 0)
  const distance = months.reduce((sum, m) => sum + m.distanceM, 0)
  const duration = months.reduce((sum, m) => sum + m.durationS, 0)
  const maximum = Math.max(1, ...months.map((m) => m[metric] ?? 0))
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
        <p className="mt-2 text-muted-foreground">{t('stats.description')}</p>
        <SlowLoading loading={years.isPending}>
          <ActivitiesEmptyState context="stats" />
        </SlowLoading>
      </section>
    )
  }

  return (
    <section>
      <Heading level={1}>{t('nav.stats')}</Heading>
      <p className="mt-2 text-muted-foreground">{t('stats.description')}</p>
      <GarminDerivedNote className="mt-1" />
      <div className="mt-6 flex flex-wrap gap-4">
        <FormField label={t('activities.year')} className="text-sm font-medium">
          {(id) => (
            <Select
              id={id}
              className="w-fit"
              value={year}
              onChange={(e) => setYear(Number(e.target.value))}
            >
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
      {years.error && (
        <p role="alert" className="mt-4 text-sm text-destructive">
          <ErrorMessage error={years.error} />{' '}
          <Button variant="secondary-outline" size="sm" onClick={() => void years.refetch()}>
            {t('activities.retry')}
          </Button>
        </p>
      )}
      <SlowLoading loading={statistics.isPending}>
        {statistics.error && (
          <p role="alert" className="mt-4 text-sm text-destructive">
            <ErrorMessage error={statistics.error} />{' '}
            <Button variant="secondary-outline" size="sm" onClick={() => void statistics.refetch()}>
              {t('activities.retry')}
            </Button>
          </p>
        )}
        {statistics.isSuccess &&
          (count === 0 ? (
            <p className="mt-8 text-muted-foreground motion-safe:animate-fade-in-up">
              {t('stats.empty')}
            </p>
          ) : (
            <div className="motion-safe:animate-fade-in-up">
              <dl className="mt-6 grid grid-cols-3 gap-3">
                {[
                  [t('stats.count'), number.format(count)],
                  [t('stats.distanceM'), format(distance, 'distanceM')],
                  [t('stats.durationS'), format(duration, 'durationS')],
                ].map(([label, value]) => (
                  <div key={label} className={card({ padding: 'compact' })}>
                    <dt className="text-xs text-muted-foreground">{label}</dt>
                    <dd className="mt-2 break-words text-lg font-semibold tabular-nums sm:text-2xl">
                      {value}
                    </dd>
                  </div>
                ))}
              </dl>
              <div className="mt-8 flex items-center justify-between gap-4">
                <Heading level={2}>{t('stats.monthly')}</Heading>
                <HelpPopover label={t('stats.methodLabel')}>{t('stats.method')}</HelpPopover>
              </div>
              <div className={card({ className: 'mt-4' })}>
                <FormField
                  layout="inline"
                  label={t('yearComparison.metric')}
                  className="justify-between text-sm font-medium"
                >
                  {(id) => (
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
                  )}
                </FormField>
                <p className="mt-3 text-xs text-muted-foreground">
                  {t('stats.chartScale', { max: format(maximum, metric) })}
                </p>
                <svg
                  viewBox="0 0 600 210"
                  className="mt-2 w-full text-primary"
                  role="img"
                  aria-label={`${t('stats.monthly')}: ${t(`stats.${metric}`)} · ${year}`}
                >
                  <line x1="0" x2="600" y1="180" y2="180" stroke="currentColor" opacity="0.2" />
                  {months.map((m, index) => {
                    const value = m[metric]
                    const height = ((value ?? 0) / maximum) * 160
                    return (
                      <g key={m.month}>
                        <title>{`${monthLabel(m.month)}: ${format(value, metric)}`}</title>
                        <rect
                          x={index * 50 + 12}
                          y={180 - height}
                          width="26"
                          height={height}
                          rx="3"
                          fill="currentColor"
                          className="transition-[y,height] duration-400 ease-out motion-reduce:transition-none"
                        />
                        <text
                          x={index * 50 + 25}
                          y="202"
                          textAnchor="middle"
                          fill="currentColor"
                          className={`text-[20px] text-muted-foreground sm:text-xs ${index % 3 ? 'hidden sm:block' : ''}`}
                        >
                          {monthLabel(m.month)}
                        </text>
                      </g>
                    )
                  })}
                </svg>
                {metric === 'avgHr' && !months.some((m) => m.avgHr !== null) && (
                  <p className="text-sm text-muted-foreground">{t('stats.noHeartRate')}</p>
                )}
              </div>
              <div className="mt-6 overflow-x-auto">
                <table className="w-full text-right text-sm tabular-nums">
                  <caption className="pb-3 text-left font-medium">
                    {t('stats.monthly')} · {year}
                  </caption>
                  <thead className="text-xs text-muted-foreground">
                    <tr>
                      <th scope="col" className="py-3 pr-3 text-left">
                        {t('stats.month')}
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
                    {months.map((m) => (
                      <tr key={m.month}>
                        <th scope="row" className="py-3 pr-3 text-left font-medium">
                          {monthLabel(m.month)}
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
      <YearComparison year={year} sportGroup={sportGroup} />
      <TrainingLoadSection />
      <RecordsSection year={year} />
    </section>
  )
}
