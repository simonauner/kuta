import { Stat } from '@/components/stat'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { ChevronDown, Flame } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Card } from '@/components/ui/card'
import { Heading } from '@/components/ui/heading'
import { SupporterBadge } from '@/components/supporter'
import { meQuery } from '@/lib/auth'
import { calendarWeeks, weekStreaks, type CalendarDay } from '@/lib/activity-calendar'
import { runnerSummaryQuery } from '@/components/runner-summary'
import { useTrainingLoad } from '@/components/training-load'
import { rollingWeekLoads, weekTrend } from '@/lib/training-load'

// The signed-in runner's own numbers and this week at a glance, beside the feed.
export function FeedSummary() {
  const { t, i18n } = useTranslation()
  const { data: me } = useQuery(meQuery)
  const { data } = useQuery({ ...runnerSummaryQuery(me?.id ?? ''), enabled: !!me })
  if (!me || !data) return null
  const number = new Intl.NumberFormat(i18n.resolvedLanguage)
  const counts = [
    { label: t('feed.summary.following'), value: data.followingCount },
    { label: t('feed.summary.followers'), value: data.followerCount },
    { label: t('feed.summary.activities'), value: data.allTime.count },
  ]

  return (
    <Card className="space-y-5">
      <Heading level={2} className="flex min-w-0 items-center justify-center gap-2 break-words">
        <Link to="/app/profile" className="underline underline-offset-4">
          {me.displayName}
        </Link>
        <SupporterBadge tier={me.supporter.tier} />
      </Heading>
      <dl className="grid grid-cols-3 divide-x text-center">
        {counts.map(({ label, value }) => (
          <Stat key={label} label={label} value={number.format(value)} size="xl" className="px-1" />
        ))}
      </dl>
      <section aria-labelledby="feed-summary-week" className="space-y-3 border-t pt-4">
        <div className="flex items-baseline justify-between gap-2">
          <Heading level={4} id="feed-summary-week">
            {t('feed.summary.thisWeek')}
          </Heading>
          <WeekStreak days={data.days} />
        </div>
        <WeekDays days={data.days} />
      </section>
      <WeeklyLoad />
      <nav aria-label={t('feed.summary.links')} className="space-y-2 border-t pt-4 text-sm">
        <Link to="/app/activities" className="block underline underline-offset-4">
          {t('feed.summary.yourActivities')}
        </Link>
        <Link to="/app/stats" className="block underline underline-offset-4">
          {t('feed.summary.yourStats')}
        </Link>
      </nav>
    </Card>
  )
}

const collapsedKey = 'kuta.feedWeekCollapsed'

// Small screens have no column beside the feed, so this week and the running load sit above it and can be folded
// away; the choice is remembered in this browser.
export function FeedWeekCard() {
  const { t } = useTranslation()
  const { data: me } = useQuery(meQuery)
  const { data } = useQuery({ ...runnerSummaryQuery(me?.id ?? ''), enabled: !!me })
  const [collapsed, setCollapsed] = useState(() => {
    try {
      return localStorage.getItem(collapsedKey) === 'true'
    } catch {
      return false
    }
  })
  if (!data) return null
  const toggle = () => {
    setCollapsed(!collapsed)
    try {
      localStorage.setItem(collapsedKey, String(!collapsed))
    } catch {
      // Blocked storage only forgets the choice.
    }
  }
  return (
    <Card>
      <div className="flex items-center justify-between gap-2">
        <Heading level={4}>
          <button
            type="button"
            aria-expanded={!collapsed}
            aria-controls="feed-week-content"
            onClick={toggle}
            className="flex items-center gap-1 rounded-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          >
            {t('feed.summary.thisWeek')}
            <ChevronDown
              aria-hidden="true"
              className={`size-4 transition-transform motion-reduce:transition-none ${collapsed ? '' : 'rotate-180'}`}
            />
          </button>
        </Heading>
        <WeekStreak days={data.days} />
      </div>
      <div id="feed-week-content" hidden={collapsed} className="mt-3 space-y-4">
        <WeekDays days={data.days} />
        <WeeklyLoad />
      </div>
    </Card>
  )
}

// sv-SE formats dates as yyyy-mm-dd in the device's time zone.
const todayLocal = () => new Date().toLocaleDateString('sv-SE')

function WeekStreak({ days }: { days: CalendarDay[] }) {
  const { t } = useTranslation()
  const streak = weekStreaks(calendarWeeks(days, todayLocal())).current
  return (
    <p className="flex items-center gap-1 text-metadata text-muted-foreground">
      <Flame aria-hidden="true" className="size-3.5 text-primary" />
      {t('profile.calendar.streak', { count: streak })}
    </p>
  )
}

function WeekDays({ days }: { days: CalendarDay[] }) {
  const { t, i18n } = useTranslation()
  const today = todayLocal()
  const week = calendarWeeks(days, today).at(-1)!
  const monday = Date.parse(`${week[0].day}T00:00:00Z`)
  const weekday = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    weekday: 'narrow',
    timeZone: 'UTC',
  })
  const longDay = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    weekday: 'long',
    timeZone: 'UTC',
  })
  return (
    <ol className="grid grid-cols-7 gap-1 text-center">
      {Array.from({ length: 7 }, (_, i) => {
        const date = new Date(monday + i * 86_400_000)
        const day = week[i]
        const active = !!day && day.count > 0
        return (
          <li key={i} className="flex flex-col items-center gap-1">
            <span aria-hidden="true" className="text-metadata text-muted-foreground">
              {weekday.format(date)}
            </span>
            <span
              className={`flex size-7 items-center justify-center rounded-full border text-metadata tabular-nums ${
                active
                  ? 'border-primary bg-primary text-primary-foreground'
                  : day?.day === today
                    ? 'border-foreground'
                    : ''
              } ${day ? '' : 'opacity-40'}`}
            >
              <span className="sr-only">
                {longDay.format(date)}:{' '}
                {active
                  ? t('feed.summary.activeDay', { count: day.count })
                  : t('feed.summary.restDay')}
              </span>
              <span aria-hidden="true">{date.getUTCDate()}</span>
            </span>
          </li>
        )
      })}
    </ol>
  )
}

// Running load per rolling seven days, with the last seven days compared to the three weeks before.
function WeeklyLoad() {
  const { t, i18n } = useTranslation()
  const { data } = useTrainingLoad()
  // Animate only when the load arrives after the card, not when it was already cached.
  const [late] = useState(() => !data)
  if (!data?.thresholdSpeedMS) return null
  const weeks = rollingWeekLoads(data.days, 4)
  if (weeks.every((w) => w.load === 0)) return null
  // Partial history makes the earliest window shorter, so compare only with four full windows.
  const trend = data.days.length >= 28 ? weekTrend(weeks.map((w) => w.load)) : null
  const maximum = Math.max(...weeks.map((w) => w.load))
  const number = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 0 })
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    day: 'numeric',
    month: 'short',
    timeZone: 'UTC',
  })

  return (
    <section
      aria-labelledby="feed-summary-load"
      className={`space-y-3 border-t pt-4 ${late ? 'motion-safe:animate-fade-in-up' : ''}`}
    >
      <Heading level={4} id="feed-summary-load">
        {t('feed.summary.load')}
      </Heading>
      {trend && (
        <div>
          <p className="text-sm font-semibold text-primary">{t(`feed.summary.trend.${trend}`)}</p>
          <p className="text-sm text-muted-foreground">{t(`feed.summary.trendText.${trend}`)}</p>
        </div>
      )}
      <ol className="flex h-24 items-end gap-2">
        {weeks.map((week, i) => {
          const current = i === weeks.length - 1
          const label = current
            ? t('feed.summary.lastSevenDays')
            : date.format(new Date(week.start))
          return (
            <li key={week.start} className="flex h-full flex-1 flex-col justify-end gap-1">
              <span className="sr-only">
                {label}: {number.format(week.load)}
              </span>
              <span
                aria-hidden="true"
                title={number.format(week.load)}
                style={{ height: `${(week.load / maximum) * 100}%` }}
                className={`min-h-0.5 rounded-sm ${current ? 'bg-primary' : 'bg-primary/30'}`}
              />
            </li>
          )
        })}
      </ol>
      <p aria-hidden="true" className="flex justify-between text-metadata text-muted-foreground">
        <span>{date.format(new Date(weeks[0].start))}</span>
        <span>{t('feed.summary.lastSevenDays')}</span>
      </p>
      <Link
        to="/app/stats/analysis"
        hash="training-load"
        className="block text-sm underline underline-offset-4"
      >
        {t('trainingLoad.warningLink')}
      </Link>
    </section>
  )
}
