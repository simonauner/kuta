import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { HelpPopover } from '@/components/help-popover'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Heading } from '@/components/ui/heading'
import { formatDuration } from '@/lib/activity'
import { aerobicChange, aerobicTrend, type AerobicPoint } from '@/lib/aerobic-efficiency'
import { api } from '@/lib/api'
import { meQuery } from '@/lib/auth'

// "2026-03" as a local month, so no time zone shifts it
const monthDate = (month: string) => new Date(`${month}-01T00:00`)

// Pace at the same heart rate per month (lib/aerobic-efficiency.ts): the trend shows fitness even without new records.
export function AerobicEfficiencySection() {
  const { t, i18n } = useTranslation()
  const { data: me } = useQuery(meQuery)
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const points = useQuery({
    queryKey: ['activities', 'aerobic-efficiency', timeZone],
    queryFn: () =>
      api.get<AerobicPoint[]>(`/api/aerobic-efficiency?${new URLSearchParams({ timeZone })}`),
  })
  const trend = points.data ? aerobicTrend(points.data) : null
  const change = trend ? aerobicChange(trend) : null
  const month = new Intl.DateTimeFormat(i18n.resolvedLanguage, { month: 'short', year: 'numeric' })
  const pace = (speedMS: number) => `${formatDuration(Math.round(1000 / speedMS))} /km`

  const width = 600
  const height = 160
  const padding = 12
  const speeds = trend?.months.map((m) => m.speedMS) ?? []
  const low = Math.min(...speeds)
  const high = Math.max(...speeds)
  // Faster is higher; a flat trend sits in the middle
  const x = (i: number) =>
    trend && trend.months.length > 1
      ? padding + (i / (trend.months.length - 1)) * (width - 2 * padding)
      : width / 2
  const y = (speed: number) =>
    high > low
      ? height - padding - ((speed - low) / (high - low)) * (height - 2 * padding)
      : height / 2

  return (
    <section id="aerobic-efficiency" className="mt-10 scroll-mt-4" aria-labelledby="aerobic-title">
      <div className="flex items-center justify-between gap-4">
        <Heading level={2} id="aerobic-title">
          {t('aerobic.title')}
        </Heading>
        <HelpPopover label={t('aerobic.methodLabel')}>{t('aerobic.method')}</HelpPopover>
      </div>
      <p className="mt-2 text-sm text-muted-foreground">{t('aerobic.description')}</p>
      {points.error && (
        <p role="alert" className="mt-4 text-sm text-destructive">
          <ErrorMessage error={points.error} />{' '}
          <Button variant="secondary-outline" size="sm" onClick={() => void points.refetch()}>
            {t('activities.retry')}
          </Button>
        </p>
      )}
      {points.data && !trend && (
        <p className="mt-4 text-muted-foreground">
          {me?.hrConsent ? t('aerobic.empty') : t('aerobic.noConsent')}
        </p>
      )}
      {trend && (
        <Card className="mt-4 motion-safe:animate-fade-in-up">
          <p className="text-sm text-muted-foreground">
            {t('aerobic.paceAt', { hr: trend.referenceHr })}
          </p>
          <p className="mt-1 text-2xl font-semibold tabular-nums">
            {change
              ? `${formatDuration(Math.round(change.from))} → ${formatDuration(Math.round(change.to))} /km`
              : pace(trend.months[0].speedMS)}
          </p>
          {change && (
            <p className="mt-1 text-sm text-muted-foreground">
              {t(
                Math.abs(change.from - change.to) < 3
                  ? 'aerobic.steady'
                  : change.to < change.from
                    ? 'aerobic.faster'
                    : 'aerobic.slower',
                {
                  from: month.format(monthDate(change.fromMonth)),
                  seconds: Math.round(Math.abs(change.from - change.to)),
                },
              )}
            </p>
          )}
          {trend.months.length > 1 && (
            <svg
              viewBox={`0 0 ${width} ${height}`}
              className="mt-4 w-full"
              role="img"
              aria-label={t('aerobic.chart', {
                months: trend.months
                  .map((m) => `${month.format(monthDate(m.month))}: ${pace(m.speedMS)}`)
                  .join(', '),
              })}
            >
              <polyline
                points={trend.months.map((m, i) => `${x(i)},${y(m.speedMS)}`).join(' ')}
                fill="none"
                stroke="var(--primary)"
                strokeWidth="2.5"
                strokeLinejoin="round"
              />
              {trend.months.map((m, i) => (
                <circle key={m.month} cx={x(i)} cy={y(m.speedMS)} r="4" fill="var(--primary)">
                  <title>{`${month.format(monthDate(m.month))}: ${pace(m.speedMS)} (${t('aerobic.runs', { count: m.count })})`}</title>
                </circle>
              ))}
            </svg>
          )}
          {trend.months.length > 1 && (
            <div className="flex justify-between text-metadata text-muted-foreground">
              <span>{month.format(monthDate(trend.months[0].month))}</span>
              <span>{month.format(monthDate(trend.months[trend.months.length - 1].month))}</span>
            </div>
          )}
        </Card>
      )}
    </section>
  )
}
