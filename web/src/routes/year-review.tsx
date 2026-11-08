import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { motion, useReducedMotion } from 'motion/react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { QueryError } from '@/components/query-error'
import { GarminDerivedNote } from '@/components/garmin'
import { SlowLoading } from '@/components/slow-loading'
import { card } from '@/components/ui/card-variants'
import { Heading } from '@/components/ui/heading'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
import { achievementText, achievementsQuery } from '@/lib/achievements'
import { activityTitle, formatDuration } from '@/lib/activity'
import { api } from '@/lib/api'
import { sportGroupLabel } from '@/lib/sport-groups'
import { recordMeters, type RecordDistance } from '@/lib/records'
import {
  distanceComparison,
  elevationComparison,
  yearChange,
  type YearReview,
} from '@/lib/year-review'

const timeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone

// One "slide" of the review: fades in as it scrolls into view.
function Slide({ title, children }: { title: ReactNode; children: ReactNode }) {
  const reduceMotion = useReducedMotion()
  return (
    <motion.section
      initial={reduceMotion ? false : { opacity: 0, y: 24 }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, amount: 0.3 }}
      transition={{ duration: 0.4, ease: 'easeOut' }}
      className={card({ className: 'mt-6' })}
    >
      <Heading level={2}>{title}</Heading>
      <div className="mt-3">{children}</div>
    </motion.section>
  )
}

function Big({ children }: { children: ReactNode }) {
  return <p className="text-4xl font-semibold tabular-nums sm:text-5xl">{children}</p>
}

export function YearReviewPage() {
  const { year: yearParam } = useParams({ from: '/app/stats/year/$year' })
  const year = Number(yearParam)
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage
  const review = useQuery({
    queryKey: ['activities', 'year-review', year, timeZone()],
    queryFn: () =>
      api.get<YearReview>(
        `/api/activities/year-review?${new URLSearchParams({ year: String(year), timeZone: timeZone() })}`,
      ),
    enabled: Number.isInteger(year),
  })
  const achievements = useQuery(achievementsQuery(timeZone()))
  const number = new Intl.NumberFormat(locale, { maximumFractionDigits: 0 })
  const decimal = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 })
  const signed = new Intl.NumberFormat(locale, {
    maximumFractionDigits: 0,
    signDisplay: 'exceptZero',
  })
  const monthName = (month: number) =>
    new Intl.DateTimeFormat(locale, { month: 'long', timeZone: 'UTC' }).format(
      new Date(Date.UTC(2026, month - 1, 1)),
    )
  const weekdayName = (weekday: number) =>
    // 5 January 2026 is a Monday
    new Intl.DateTimeFormat(locale, { weekday: 'long', timeZone: 'UTC' }).format(
      new Date(Date.UTC(2026, 0, 4 + weekday)),
    )
  const km = (meters: number) => `${number.format(meters / 1000)} km`

  const data = review.data
  const summary = data?.summary
  const totals = summary?.totals
  const yearAchievements =
    achievements.data?.filter((a) => new Date(a.achievedAt).getFullYear() === year) ?? []
  const biggestMonth = summary?.months.reduce((best, m) =>
    m.durationS > best.durationS ? m : best,
  )
  const maxMonth = Math.max(1, ...(summary?.months.map((m) => m.durationS) ?? []))
  const distanceChange =
    data && totals ? yearChange(totals.distanceM, data.previous?.distanceM) : null
  const distance = totals ? distanceComparison(totals.distanceM) : null
  const climb = totals ? elevationComparison(totals.elevationM) : null
  const returnTo = `/app/stats/year/${year}`
  const activityLink = (activity: {
    activityId: string
    title: string | null
    sport: string
    startedAt: string
  }) => (
    <Link
      to="/app/activities/$activityId"
      params={{ activityId: activity.activityId }}
      search={{ returnTo }}
      className="underline underline-offset-4"
    >
      {activityTitle(
        { title: activity.title, sport: activity.sport, startedAt: activity.startedAt },
        t,
      )}
    </Link>
  )

  return (
    <section>
      <Breadcrumb aria-label={t('breadcrumbs')} className="mb-4">
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link to="/app/stats" activeOptions={{ exact: true }} />}>
              {t('nav.stats')}
            </BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>{t('yearReview.breadcrumb', { year })}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <div className="flex flex-wrap items-baseline justify-between gap-3">
        <Heading level={1}>
          {t(data && !data.complete ? 'yearReview.titleSoFar' : 'yearReview.title', { year })}
        </Heading>
        <nav aria-label={t('yearReview.years')} className="flex gap-3 text-sm">
          <Link
            to="/app/stats/year/$year"
            params={{ year: String(year - 1) }}
            className="underline underline-offset-4"
          >
            ← {year - 1}
          </Link>
          {data && !data.complete ? null : (
            <Link
              to="/app/stats/year/$year"
              params={{ year: String(year + 1) }}
              className="underline underline-offset-4"
            >
              {year + 1} →
            </Link>
          )}
        </nav>
      </div>
      <GarminDerivedNote className="mt-1" />
      <QueryError query={review} className="mt-4" />
      <SlowLoading loading={review.isPending && Number.isInteger(year)}>{null}</SlowLoading>
      {data && totals && summary && totals.count === 0 && (
        <p className="mt-6 text-muted-foreground">{t('yearReview.empty', { year })}</p>
      )}
      {data && totals && summary && totals.count > 0 && (
        <>
          <Slide title={t('yearReview.totalsTitle')}>
            <Big>{km(totals.distanceM)}</Big>
            <p className="mt-2 text-body">
              {t('yearReview.totals', {
                count: totals.count,
                hours: number.format(totals.durationS / 3600),
                days: totals.activeDays,
              })}
            </p>
            {distanceChange !== null && (
              <p className="mt-1 text-sm text-muted-foreground">
                {t('yearReview.change', {
                  percent: signed.format(distanceChange),
                  previous: year - 1,
                })}
              </p>
            )}
          </Slide>

          {(distance || climb) && (
            <Slide title={t('yearReview.comparisonsTitle')}>
              <ul className="space-y-3 text-body">
                {distance && (
                  <li>
                    {t(`yearReview.distanceComparison.${distance.key}`, {
                      times: decimal.format(distance.times),
                    })}
                  </li>
                )}
                {climb && (
                  <li>
                    {t(`yearReview.climbComparison.${climb.key}`, {
                      meters: number.format(totals.elevationM),
                      times: decimal.format(climb.times),
                    })}
                  </li>
                )}
                <li>
                  {t('yearReview.timeComparison', {
                    days: decimal.format(totals.durationS / 86_400),
                  })}
                </li>
              </ul>
            </Slide>
          )}

          <Slide title={t('yearReview.sportsTitle')}>
            <ul className="space-y-3">
              {summary.sports.slice(0, 5).map((s) => (
                <li key={s.sportGroup}>
                  <div className="flex justify-between gap-3 text-sm">
                    <span className="font-medium">{sportGroupLabel(s.sportGroup, t)}</span>
                    <span className="text-muted-foreground tabular-nums">
                      {t('yearReview.sportLine', {
                        count: s.count,
                        time: formatDuration(Math.round(s.durationS)),
                      })}
                      {s.distanceM > 0 && ` · ${km(s.distanceM)}`}
                    </span>
                  </div>
                  <div className="mt-1 h-2 overflow-hidden rounded-full bg-muted">
                    <div
                      className="h-full rounded-full bg-primary"
                      style={{ width: `${(s.durationS / summary.sports[0].durationS) * 100}%` }}
                    />
                  </div>
                </li>
              ))}
            </ul>
          </Slide>

          <Slide title={t('yearReview.monthsTitle')}>
            {biggestMonth && biggestMonth.durationS > 0 && (
              <p className="text-body">
                {t('yearReview.biggestMonth', {
                  month: monthName(biggestMonth.month),
                  time: formatDuration(Math.round(biggestMonth.durationS)),
                  count: biggestMonth.count,
                })}
              </p>
            )}
            <ol
              className="mt-4 grid h-32 grid-cols-12 items-end gap-1"
              aria-label={t('yearReview.monthsChart')}
            >
              {summary.months.map((m) => (
                <li key={m.month} className="flex h-full flex-col justify-end">
                  <span className="sr-only">
                    {`${monthName(m.month)}: ${formatDuration(Math.round(m.durationS))}`}
                  </span>
                  <span
                    aria-hidden="true"
                    title={`${monthName(m.month)}: ${formatDuration(Math.round(m.durationS))}`}
                    className={`rounded-t ${m === biggestMonth ? 'bg-primary' : 'bg-primary/40'}`}
                    style={{
                      height: `${Math.max(m.durationS > 0 ? 4 : 0, (m.durationS / maxMonth) * 100)}%`,
                    }}
                  />
                </li>
              ))}
            </ol>
            <div
              aria-hidden="true"
              className="mt-1 grid grid-cols-12 gap-1 text-center text-metadata text-muted-foreground"
            >
              {summary.months.map((m) => (
                <span key={m.month}>{monthName(m.month).slice(0, 1).toUpperCase()}</span>
              ))}
            </div>
          </Slide>

          <Slide title={t('yearReview.habitsTitle')}>
            <dl className="grid gap-4 sm:grid-cols-2">
              {summary.favoriteWeekday !== null && (
                <div>
                  <dt className="text-metadata text-muted-foreground">{t('yearReview.weekday')}</dt>
                  <dd className="font-semibold first-letter:uppercase">
                    {weekdayName(summary.favoriteWeekday)}
                  </dd>
                  <dd className="text-sm text-muted-foreground">
                    {t('yearReview.activities', { count: summary.favoriteWeekdayCount })}
                  </dd>
                </div>
              )}
              {summary.timeOfDay !== null && (
                <div>
                  <dt className="text-metadata text-muted-foreground">
                    {t('yearReview.timeOfDay')}
                  </dt>
                  <dd className="font-semibold">
                    {t(`yearReview.timesOfDay.${summary.timeOfDay}`)}
                  </dd>
                  <dd className="text-sm text-muted-foreground">
                    {t('yearReview.activities', { count: summary.timeOfDayCount })}
                  </dd>
                </div>
              )}
              <div>
                <dt className="text-metadata text-muted-foreground">{t('yearReview.dayStreak')}</dt>
                <dd className="font-semibold">
                  {t('yearReview.days', { count: summary.longestDayStreak })}
                </dd>
              </div>
              <div>
                <dt className="text-metadata text-muted-foreground">
                  {t('yearReview.weekStreak')}
                </dt>
                <dd className="font-semibold">
                  {t('yearReview.weeks', { count: summary.longestWeekStreak })}
                </dd>
              </div>
            </dl>
          </Slide>

          {(summary.longestRun || summary.longestActivity) && (
            <Slide title={t('yearReview.highlightsTitle')}>
              <dl className="space-y-3">
                {summary.longestRun && (
                  <div>
                    <dt className="text-metadata text-muted-foreground">
                      {t('yearReview.longestRun')}
                    </dt>
                    <dd className="font-semibold tabular-nums">
                      {km(summary.longestRun.distanceM)}
                    </dd>
                    <dd className="text-sm">{activityLink(summary.longestRun)}</dd>
                  </div>
                )}
                {summary.longestActivity && (
                  <div>
                    <dt className="text-metadata text-muted-foreground">
                      {t('yearReview.longestActivity')}
                    </dt>
                    <dd className="font-semibold tabular-nums">
                      {formatDuration(Math.round(summary.longestActivity.durationS))}
                    </dd>
                    <dd className="text-sm">{activityLink(summary.longestActivity)}</dd>
                  </div>
                )}
              </dl>
            </Slide>
          )}

          {data.records.length > 0 && (
            <Slide title={t('yearReview.recordsTitle')}>
              <ul className="divide-y text-sm tabular-nums">
                {data.records.map((r) => (
                  <li
                    key={r.distanceM}
                    className="flex flex-wrap items-center justify-between gap-2 py-2"
                  >
                    <span className="font-medium">
                      {t(`records.distances.${r.distanceM as RecordDistance}`)}
                    </span>
                    <span>
                      {formatDuration(Math.round(r.elapsedS))}
                      <span className="ml-2 text-muted-foreground">
                        {formatDuration(
                          Math.round(r.elapsedS / (recordMeters(r.distanceM) / 1000)),
                        )}{' '}
                        /km
                      </span>
                      {r.personalBest && (
                        <span className="ml-2 rounded-full bg-primary/10 px-2 py-0.5 text-xs font-medium text-primary">
                          {t('yearReview.personalBest')}
                        </span>
                      )}
                    </span>
                  </li>
                ))}
              </ul>
            </Slide>
          )}

          {summary.races.length > 0 && (
            <Slide title={t('yearReview.racesTitle', { count: summary.races.length })}>
              <ul className="space-y-2 text-sm">
                {summary.races.map((r) => (
                  <li key={r.activityId} className="flex flex-wrap justify-between gap-2">
                    {activityLink(r)}
                    <span className="text-muted-foreground tabular-nums">
                      {r.distanceM > 0 && `${km(r.distanceM)} · `}
                      {formatDuration(Math.round(r.durationS))}
                    </span>
                  </li>
                ))}
              </ul>
            </Slide>
          )}

          {data.topEquipment && (
            <Slide title={t('yearReview.equipmentTitle')}>
              <Big>{data.topEquipment.name}</Big>
              <p className="mt-2 text-sm text-muted-foreground">
                {t('yearReview.equipment', {
                  count: data.topEquipment.count,
                  distance: km(data.topEquipment.distanceM),
                })}
              </p>
            </Slide>
          )}

          {yearAchievements.length > 0 && (
            <Slide title={t('yearReview.achievementsTitle', { count: yearAchievements.length })}>
              <ul className="flex flex-wrap gap-2">
                {yearAchievements.map((a) => (
                  <li
                    key={a.key}
                    className="rounded-full bg-primary/10 px-3 py-1 text-sm font-medium text-primary"
                  >
                    {achievementText(a, t, locale).title}
                  </li>
                ))}
              </ul>
            </Slide>
          )}
        </>
      )}
    </section>
  )
}
