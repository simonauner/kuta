import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Heading } from '@/components/ui/heading'
import { api } from '@/lib/api'
import { calendarWeeks, weekStreaks, type CalendarDay } from '@/lib/activity-calendar'

type RunnerSummary = { days: CalendarDay[] }

const levelClass = ['bg-muted', 'bg-primary/25', 'bg-primary/50', 'bg-primary/75', 'bg-primary']

export function runnerSummaryQuery(runnerId: string) {
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  return {
    queryKey: ['runner', runnerId, 'summary', timeZone],
    queryFn: () =>
      api.get<RunnerSummary>(
        `/api/runners/${runnerId}/summary?${new URLSearchParams({ timeZone })}`,
      ),
  }
}

// A year of activity, one square per day, coloured by distance.
export function ActivityCalendar({ runnerId }: { runnerId: string }) {
  const { t, i18n } = useTranslation()
  const summary = useQuery(runnerSummaryQuery(runnerId))
  if (summary.error)
    return (
      <p role="alert" className="text-sm text-destructive">
        <ErrorMessage error={summary.error} />
      </p>
    )
  if (!summary.data) return null
  // sv-SE formats dates as yyyy-mm-dd in the device's time zone.
  const weeks = calendarWeeks(summary.data.days, new Date().toLocaleDateString('sv-SE'))
  const streaks = weekStreaks(weeks)
  const activeDays = weeks.flat().filter((d) => d.count > 0).length
  const month = new Intl.DateTimeFormat(i18n.resolvedLanguage, { month: 'short', timeZone: 'UTC' })
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    dateStyle: 'medium',
    timeZone: 'UTC',
  })
  const km = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 1 })
  const describe = (d: CalendarDay) =>
    `${date.format(new Date(d.day))}: ${t('profile.calendar.day', { count: d.count, distance: km.format(d.distanceM / 1000) })}`

  return (
    <section aria-labelledby="calendar-title" className="space-y-3">
      <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
        <Heading level={2} id="calendar-title">
          {t('profile.calendar.title')}
        </Heading>
        <p className="text-metadata text-muted-foreground">
          {t('profile.calendar.activeDays', { count: activeDays })} ·{' '}
          {t('profile.calendar.streak', { count: streaks.current })} ·{' '}
          {t('profile.calendar.longest', { count: streaks.longest })}
        </p>
      </div>
      <div
        role="img"
        aria-label={t('profile.calendar.summary', { count: activeDays })}
        className="grid grid-flow-col gap-[2px]"
        style={{ gridTemplateColumns: `repeat(${weeks.length}, minmax(0, 1fr))` }}
      >
        {weeks.map((week) => (
          <div key={week[0].day} className="grid grid-cols-1 content-start gap-[2px]">
            <span className="h-4 overflow-visible whitespace-nowrap text-metadata text-muted-foreground">
              {Number(week[0].day.slice(8)) <= 7 ? month.format(new Date(week[0].day)) : ''}
            </span>
            {week.map((d) => (
              <span
                key={d.day}
                title={describe(d)}
                className={`block aspect-square rounded-[2px] ${levelClass[d.level]}`}
              />
            ))}
          </div>
        ))}
      </div>
    </section>
  )
}
