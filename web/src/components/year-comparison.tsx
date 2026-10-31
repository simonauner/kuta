import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import { formatDuration } from '@/lib/activity'
import {
  relativeChange,
  spreadLabels,
  yearComparison,
  yearMetrics,
  type DailyTotal,
  type YearMetric,
} from '@/lib/year-comparison'

const selectClass = 'rounded-md border bg-background px-3 py-2 text-sm text-foreground'
const shownYears = 5

// Ackumulerad mängd per år på samma kalender, med valt år framhävt och jämförelse vid samma datum.
export function YearComparison({ year, sportGroup }: { year: number; sportGroup: string }) {
  const { t, i18n } = useTranslation()
  const [metric, setMetric] = useState<YearMetric>('distanceM')
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const days = useQuery({
    queryKey: ['activities', 'statistics', 'days', timeZone, sportGroup],
    queryFn: () => {
      const params = new URLSearchParams({ timeZone })
      if (sportGroup) params.set('sportGroup', sportGroup)
      return api.get<DailyTotal[]>(`/api/activities/statistics/days?${params}`)
    },
  })
  const [today] = useState(() => {
    const now = new Date()
    return { year: now.getFullYear(), month: now.getMonth() + 1, day: now.getDate() }
  })
  const number = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 1 })
  const percent = new Intl.NumberFormat(i18n.resolvedLanguage, {
    style: 'percent',
    maximumFractionDigits: 0,
    signDisplay: 'exceptZero',
  })
  const dayMonth = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    day: 'numeric',
    month: 'short',
  })
  const monthFormat = new Intl.DateTimeFormat(i18n.resolvedLanguage, { month: 'short' })
  const format = (value: number) => {
    if (metric === 'distanceM') return `${number.format(value / 1000)} km`
    if (metric === 'durationS') return formatDuration(value)
    return number.format(value)
  }

  const series = days.data ? yearComparison(days.data, metric, today) : []
  const shown = series.filter((s, i) => i < shownYears || s.year === year)
  const width = 600
  const height = 220
  const pad = { top: 8, right: 44, bottom: 22, left: 4 }
  const maximum = Math.max(1, ...shown.map((s) => s.total))
  const x = (day: number) => pad.left + (day / 366) * (width - pad.left - pad.right)
  const y = (value: number) => pad.top + (1 - value / maximum) * (height - pad.top - pad.bottom)
  // Valt år ritas sist så att det hamnar överst; övriga år tonas efter hur gamla de är.
  const drawOrder = [...shown].sort((a, b) =>
    a.year === year ? 1 : b.year === year ? -1 : a.year - b.year,
  )
  const labelY = spreadLabels(
    drawOrder.map((s) => y(s.points[s.points.length - 1].value) + 4),
    14,
    pad.top + 8,
    height - pad.bottom,
  )
  const opacity = (index: number) => 0.6 - Math.min(index, 4) * 0.1
  const cutoff = dayMonth.format(new Date(2000, today.month - 1, today.day))

  return (
    <section className="mt-10" aria-labelledby="year-comparison-title">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 id="year-comparison-title" className="text-xl font-semibold tracking-tight">
            {t('yearComparison.title')}
          </h2>
          <p className="mt-2 text-sm text-muted-foreground">{t('yearComparison.description')}</p>
        </div>
        <label className="grid gap-1 text-sm font-medium">
          {t('yearComparison.metric')}
          <select
            className={selectClass}
            value={metric}
            onChange={(e) => setMetric(e.target.value as YearMetric)}
          >
            {yearMetrics.map((key) => (
              <option key={key} value={key}>
                {t(`yearComparison.metrics.${key}`)}
              </option>
            ))}
          </select>
        </label>
      </div>
      {days.error && (
        <p role="alert" className="mt-4 text-sm text-destructive">
          <ErrorMessage error={days.error} />{' '}
          <Button variant="outline" size="sm" onClick={() => void days.refetch()}>
            {t('activities.retry')}
          </Button>
        </p>
      )}
      {days.isSuccess &&
        (series.length === 0 ? (
          <p className="mt-4 text-muted-foreground">{t('yearComparison.empty')}</p>
        ) : (
          <>
            <div className="mt-4 rounded-xl border bg-card p-4">
              <p className="text-xs text-muted-foreground">
                {t('stats.chartScale', { max: format(maximum) })}
              </p>
              <svg
                viewBox={`0 0 ${width} ${height}`}
                className="mt-2 w-full overflow-visible"
                role="img"
                aria-label={t('yearComparison.chart', {
                  metric: t(`yearComparison.metrics.${metric}`),
                })}
              >
                <line
                  x1={pad.left}
                  x2={width - pad.right}
                  y1={y(0)}
                  y2={y(0)}
                  stroke="currentColor"
                  className="text-muted-foreground"
                  opacity="0.3"
                />
                {[1, 4, 7, 10].map((month) => {
                  const day = (Date.UTC(2001, month - 1, 1) - Date.UTC(2001, 0, 1)) / 86_400_000
                  return (
                    <text
                      key={month}
                      x={x(day)}
                      y={height - 4}
                      fill="currentColor"
                      className="text-[20px] text-muted-foreground sm:text-xs"
                    >
                      {monthFormat.format(new Date(2001, month - 1, 1))}
                    </text>
                  )
                })}
                {drawOrder.map((s, index) => {
                  const selected = s.year === year
                  const age = series.findIndex((other) => other.year === s.year)
                  const end = s.points[s.points.length - 1]
                  return (
                    <g
                      key={s.year}
                      className={selected ? 'text-primary' : 'text-muted-foreground'}
                      opacity={selected ? 1 : opacity(age)}
                    >
                      <title>{`${s.year}: ${format(s.total)}`}</title>
                      <polyline
                        points={s.points.map((p) => `${x(p.day)},${y(p.value)}`).join(' ')}
                        fill="none"
                        stroke="currentColor"
                        strokeWidth={selected ? 3 : 1.5}
                        strokeLinejoin="round"
                      />
                      <text
                        x={x(end.day) + 6}
                        y={labelY[index]}
                        fill="currentColor"
                        className={`text-[20px] tabular-nums sm:text-xs ${selected ? 'font-semibold' : ''}`}
                      >
                        {s.year}
                      </text>
                    </g>
                  )
                })}
              </svg>
            </div>
            <div className="mt-4 overflow-x-auto">
              <table className="w-full text-right text-sm tabular-nums">
                <caption className="pb-3 text-left font-medium">
                  {t('yearComparison.table', { metric: t(`yearComparison.metrics.${metric}`) })}
                </caption>
                <thead className="text-xs text-muted-foreground">
                  <tr>
                    <th scope="col" className="py-3 pr-3 text-left">
                      {t('activities.year')}
                    </th>
                    <th scope="col" className="px-2">
                      {t('yearComparison.toDate', { date: cutoff })}
                    </th>
                    <th scope="col" className="px-2">
                      {t('yearComparison.change')}
                    </th>
                    <th scope="col" className="px-2">
                      {t('yearComparison.fullYear')}
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {series.map((s, i) => {
                    const previous = series[i + 1]?.year === s.year - 1 ? series[i + 1] : undefined
                    const change = relativeChange(s.toDate, previous?.toDate)
                    return (
                      <tr key={s.year} className={s.year === year ? 'font-semibold' : undefined}>
                        <th scope="row" className="py-3 pr-3 text-left font-medium">
                          {s.year}
                        </th>
                        <td className="px-2">{format(s.toDate)}</td>
                        <td className="px-2">{change === null ? '–' : percent.format(change)}</td>
                        <td className="px-2">{s.year === today.year ? '–' : format(s.total)}</td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
            <p className="mt-4 text-xs leading-relaxed text-muted-foreground">
              {t('yearComparison.method', { years: shownYears })}
            </p>
          </>
        ))}
    </section>
  )
}
