import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { api } from '@/lib/api'
import { activityTitle, formatDuration, type ActivityTag } from '@/lib/activity'
import type { Sport } from '@/lib/sports'
import { SportIcon } from '@/components/sport-icon'
import { ErrorMessage } from '@/components/error-message'
import { SlowLoading } from '@/components/slow-loading'
import { Button } from '@/components/ui/button'
import { Heading } from '@/components/ui/heading'
import { Modal } from '@/components/ui/modal'

type CalendarActivity = {
  id: string
  sport: string
  title: string | null
  startedAt: string
  day: string
  durationS: number
  distanceM: number
  attribution: string | null
}

const pad = (n: number) => String(n).padStart(2, '0')

// A year as twelve month calendars; days with activities open that day's list.
export function ActivityCalendar({
  year,
  tag,
  sport,
}: {
  year: number
  tag?: ActivityTag
  sport?: Sport
}) {
  const { t, i18n } = useTranslation()
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const calendar = useQuery({
    queryKey: ['activities', 'calendar', timeZone, year, tag, sport],
    queryFn: () => {
      const params = new URLSearchParams({ timeZone, year: String(year) })
      if (tag) params.set('tag', tag)
      if (sport) params.set('sport', sport)
      return api.get<CalendarActivity[]>(`/api/activities/calendar?${params}`)
    },
  })
  const byDay = new Map<string, CalendarActivity[]>()
  for (const a of calendar.data ?? []) byDay.set(a.day, [...(byDay.get(a.day) ?? []), a])
  const [day, setDay] = useState<string | null>(null)
  const dialog = useRef<HTMLDialogElement>(null)
  const monthFormat = new Intl.DateTimeFormat(i18n.resolvedLanguage, { month: 'long' })
  const dayFormat = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'full' })
  const timeFormat = new Intl.DateTimeFormat(i18n.resolvedLanguage, { timeStyle: 'short' })
  const weekdayFormat = new Intl.DateTimeFormat(i18n.resolvedLanguage, { weekday: 'narrow' })
  const kmFormat = new Intl.NumberFormat(i18n.resolvedLanguage, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
  // 2024-01-01 was a Monday; weeks start on Monday as in the list's ISO weeks.
  const weekdays = Array.from({ length: 7 }, (_, i) =>
    weekdayFormat.format(new Date(2024, 0, 1 + i)),
  )
  const selected = day ? (byDay.get(day) ?? []) : []

  return (
    <SlowLoading loading={calendar.isPending}>
      {calendar.error && (
        <p role="alert" className="mt-4 text-sm text-destructive">
          <ErrorMessage error={calendar.error} />{' '}
          <Button
            variant="secondary-outline"
            size="sm"
            disabled={calendar.isFetching}
            onClick={() => void calendar.refetch()}
          >
            {t('activities.retry')}
          </Button>
        </p>
      )}
      {calendar.isSuccess && (
        <>
          <p className="mt-6 text-metadata text-muted-foreground">
            {t('activities.calendarCount', { count: calendar.data.length, year })}
          </p>
          <div className="mt-4 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            {Array.from({ length: 12 }, (_, month) => {
              const first = new Date(year, month, 1)
              const days = new Date(year, month + 1, 0).getDate()
              const offset = (first.getDay() + 6) % 7
              return (
                <div key={month}>
                  <Heading level={3} className="mb-2 capitalize">
                    {monthFormat.format(first)}
                  </Heading>
                  <div className="grid grid-cols-7 gap-1 text-center text-metadata">
                    {weekdays.map((name, i) => (
                      <span key={i} aria-hidden className="text-muted-foreground">
                        {name}
                      </span>
                    ))}
                    {Array.from({ length: offset }, (_, i) => (
                      <span key={`empty-${i}`} />
                    ))}
                    {Array.from({ length: days }, (_, i) => {
                      const date = `${year}-${pad(month + 1)}-${pad(i + 1)}`
                      const count = byDay.get(date)?.length ?? 0
                      if (count === 0)
                        return (
                          <span key={date} className="py-1.5 text-muted-foreground">
                            {i + 1}
                          </span>
                        )
                      return (
                        <button
                          key={date}
                          type="button"
                          className="relative rounded-md bg-primary/15 py-1.5 font-semibold text-primary transition-colors hover:bg-primary/25 focus-visible:outline-2 focus-visible:outline-ring"
                          aria-label={t('activities.calendarDay', {
                            date: dayFormat.format(new Date(year, month, i + 1)),
                            count,
                          })}
                          onClick={() => {
                            setDay(date)
                            dialog.current?.showModal()
                          }}
                        >
                          {i + 1}
                          {count > 1 && (
                            <span className="absolute -top-1 -right-1 rounded-full bg-primary px-1 text-[10px] leading-4 text-primary-foreground">
                              {count}
                            </span>
                          )}
                        </button>
                      )
                    })}
                  </div>
                </div>
              )
            })}
          </div>
        </>
      )}
      <Modal ref={dialog} aria-labelledby="calendar-day-title" onClose={() => setDay(null)}>
        <Heading level={3} id="calendar-day-title" className="first-letter:uppercase">
          {day && dayFormat.format(new Date(`${day}T12:00`))}
        </Heading>
        <ul className="mt-3 divide-y">
          {selected.map((a) => (
            <li key={a.id} className="py-3">
              <Link
                to="/app/activities/$activityId"
                params={{ activityId: a.id }}
                search={{ year, tag, sport, view: 'calendar' }}
                className="inline-flex items-center gap-2 font-medium underline underline-offset-2"
              >
                {activityTitle(a, t)}
                <SportIcon sport={a.sport} />
              </Link>
              {a.attribution && (
                <p className="text-metadata text-muted-foreground">{a.attribution}</p>
              )}
              <p className="text-metadata text-muted-foreground">
                {timeFormat.format(new Date(a.startedAt))} · {kmFormat.format(a.distanceM / 1000)}{' '}
                km · {formatDuration(a.durationS)}
              </p>
            </li>
          ))}
        </ul>
        <div className="mt-4 flex justify-end">
          <Button variant="secondary-outline" onClick={() => dialog.current?.close()}>
            {t('activities.close')}
          </Button>
        </div>
      </Modal>
    </SlowLoading>
  )
}
