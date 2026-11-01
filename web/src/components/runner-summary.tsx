import { useQuery } from '@tanstack/react-query'
import { Link, useRouterState } from '@tanstack/react-router'
import { Trophy } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { SportIcon } from '@/components/sport-icon'
import { card } from '@/components/ui/card-variants'
import { Heading } from '@/components/ui/heading'
import { api } from '@/lib/api'
import { meQuery } from '@/lib/auth'
import { formatDuration, formatPace } from '@/lib/activity'
import { recordMeters, type RecordDistance, type StoredBestEffort } from '@/lib/records'
import { calendarWeeks, weekStreaks, type CalendarDay } from '@/lib/activity-calendar'

type Totals = { count: number; distanceM: number; durationS: number }
type RunnerSummary = {
  joinedAt: string
  thisYear: Totals
  allTime: Totals
  topSports: { sport: string; count: number }[]
  days: CalendarDay[]
  records: StoredBestEffort[]
  followerCount: number
  followingCount: number
}

const levelClass = ['bg-muted', 'bg-primary/25', 'bg-primary/50', 'bg-primary/75', 'bg-primary']

// The owner's preview of their profile as others see it.
export type ViewAs = 'followers' | 'public'
type ProfileProps = { runnerId: string; viewAs?: ViewAs }

export function runnerSummaryQuery(runnerId: string, viewAs?: ViewAs) {
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const params = new URLSearchParams({ timeZone })
  if (viewAs) params.set('viewAs', viewAs)
  return {
    queryKey: ['runner', runnerId, 'summary', timeZone, viewAs],
    queryFn: () => api.get<RunnerSummary>(`/api/runners/${runnerId}/summary?${params}`),
  }
}

// Member since, distance, sessions and time this year and in total, and the most common sports.
export function ProfileStats({ runnerId, viewAs }: ProfileProps) {
  const { t, i18n } = useTranslation()
  const { data } = useQuery(runnerSummaryQuery(runnerId, viewAs))
  const { data: me } = useQuery(meQuery)
  if (!data) return null
  const follows = `${t('profile.stats.followers', { count: data.followerCount })} · ${t('profile.stats.following', { count: data.followingCount })}`
  const language = i18n.resolvedLanguage
  const km = new Intl.NumberFormat(language, {
    style: 'unit',
    unit: 'kilometer',
    maximumFractionDigits: 0,
  })
  const hours = new Intl.NumberFormat(language, {
    style: 'unit',
    unit: 'hour',
    maximumFractionDigits: 0,
  })
  const since = new Intl.DateTimeFormat(language, {
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  })
  const tile = (label: string, totals: Totals) => (
    <div className={card()}>
      <p className="text-metadata text-muted-foreground">{label}</p>
      <p className="text-2xl font-semibold tabular-nums">{km.format(totals.distanceM / 1000)}</p>
      <p className="text-metadata text-muted-foreground">
        {t('profile.stats.sessions', { count: totals.count })} ·{' '}
        {hours.format(totals.durationS / 3600)}
      </p>
    </div>
  )
  return (
    <div className="space-y-3">
      <p className="text-metadata text-muted-foreground">
        {t('profile.stats.memberSince', { date: since.format(new Date(data.joinedAt)) })} ·{' '}
        {me?.id === runnerId && !viewAs ? (
          <Link to="/app/settings/followers" className="underline underline-offset-2">
            {follows}
          </Link>
        ) : (
          follows
        )}
      </p>
      <div className="grid grid-cols-2 gap-3">
        {tile(t('profile.stats.thisYear'), data.thisYear)}
        {tile(t('profile.stats.allTime'), data.allTime)}
      </div>
      {data.topSports.length > 0 && (
        <ul aria-label={t('profile.stats.topSports')} className="flex flex-wrap gap-2">
          {data.topSports.map(({ sport, count }) => (
            <li
              key={sport}
              className="flex items-center gap-1.5 rounded-full border px-3 py-1 text-sm"
            >
              <SportIcon sport={sport} className="size-4" />
              {t(`newActivity.sports.${sport}`, { defaultValue: sport })}
              <span className="text-muted-foreground tabular-nums">{count}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

// Personal bests as a row of cards, each linking to the activity it was set in.
export function RecordShelf({ runnerId, viewAs }: ProfileProps) {
  const { t, i18n } = useTranslation()
  const returnTo = useRouterState({ select: (s) => s.location.href })
  const { data } = useQuery(runnerSummaryQuery(runnerId, viewAs))
  if (!data?.records.length) return null
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })
  return (
    <section aria-labelledby="record-shelf-title" className="space-y-3">
      <Heading level={2} id="record-shelf-title">
        {t('profile.records.title')}
      </Heading>
      <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3">
        {data.records.map((record) => (
          <li key={record.distanceM}>
            <Link
              to="/app/activities/$activityId"
              params={{ activityId: record.activityId }}
              search={{ returnTo }}
              className={card({ interactive: true, className: 'block h-full' })}
            >
              <p className="flex items-center gap-1.5 text-metadata text-muted-foreground">
                <Trophy aria-hidden="true" className="size-3.5 text-primary" />
                {t(`records.distances.${record.distanceM as RecordDistance}`)}
              </p>
              <p className="text-2xl font-semibold tabular-nums">
                {formatDuration(record.elapsedS)}
              </p>
              <p className="text-metadata text-muted-foreground">
                {formatPace(record.elapsedS, recordMeters(record.distanceM))} ·{' '}
                {date.format(new Date(record.achievedAt))}
              </p>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  )
}

// A year of activity, one square per day, coloured by distance.
export function ActivityCalendar({ runnerId, viewAs }: ProfileProps) {
  const { t, i18n } = useTranslation()
  const summary = useQuery(runnerSummaryQuery(runnerId, viewAs))
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
