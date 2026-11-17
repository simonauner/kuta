import { useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useRouterState } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { Avatar } from '@/components/avatar'
import { ErrorText } from '@/components/error-message'
import { HelpPopover } from '@/components/help-popover'
import { Input } from '@/components/ui/input'
import { QueryError } from '@/components/query-error'
import { Button } from '@/components/ui/button'
import { card } from '@/components/ui/card-variants'
import { FormField } from '@/components/ui/form-field'
import { Heading } from '@/components/ui/heading'
import { Modal } from '@/components/ui/modal'
import { Select } from '@/components/ui/select'
import { api } from '@/lib/api'
import { refreshActivityQueries } from '@/lib/activity-query'
import {
  activityTitle,
  formatDuration,
  type ActivityDetail,
  type ActivityPage,
} from '@/lib/activity'
import { orienteeringLegs } from '@/lib/orienteering'

export function CourseLengthField({
  value,
  onChange,
  disabled,
  className,
}: {
  value: string
  onChange: (km: string) => void
  disabled?: boolean
  className?: string
}) {
  const { t } = useTranslation()
  return (
    <div className={`space-y-2 ${className ?? ''}`}>
      <FormField label={t('orienteering.courseLength')}>
        {(id) => (
          <Input
            id={id}
            type="number"
            inputMode="decimal"
            min={0.01}
            max={100}
            step={0.01}
            className="w-32"
            value={value}
            onChange={(event) => onChange(event.currentTarget.value)}
            disabled={disabled}
          />
        )}
      </FormField>
      <p className="text-xs text-muted-foreground">{t('orienteering.courseLengthHint')}</p>
    </div>
  )
}

// Legs from the watch's laps, with probable misses where GPS gives the straight line (only for the owner).
export function OrienteeringLegs({ activity, own }: { activity: ActivityDetail; own: boolean }) {
  const { t, i18n } = useTranslation()
  if (activity.laps.length < 2)
    return own ? (
      <p className="mt-6 text-sm text-muted-foreground">{t('orienteering.noLegs')}</p>
    ) : null
  const legs = orienteeringLegs(activity.laps, activity.streams)
  const number = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 0 })
  const percent = new Intl.NumberFormat(i18n.resolvedLanguage, {
    style: 'percent',
    signDisplay: 'always',
    maximumFractionDigits: 0,
  })
  const straight = legs.some((l) => l.straightM !== null)
  const still = legs.some((l) => l.stillS !== null)
  return (
    <section className={card({ className: 'mt-6' })} aria-labelledby="legs-title">
      <div className="flex items-center gap-1">
        <Heading level={2} id="legs-title">
          {t('orienteering.legs')}
        </Heading>
        <HelpPopover label={t('orienteering.legsHelpLabel')}>
          {t('orienteering.legsHelp')}
          {straight && ` ${t('orienteering.missHelp')}`}
        </HelpPopover>
      </div>
      <div className="mt-3 overflow-x-auto">
        <table className="w-full text-sm whitespace-nowrap tabular-nums">
          <thead className="text-left text-xs text-muted-foreground">
            <tr>
              <th className="py-1 pr-3 font-normal">{t('orienteering.leg')}</th>
              <th className="py-1 pr-3 font-normal">{t('activity.time')}</th>
              <th className="py-1 pr-3 font-normal">{t('orienteering.run')}</th>
              {straight && (
                <>
                  <th className="py-1 pr-3 font-normal">{t('orienteering.straight')}</th>
                  <th className="py-1 pr-3 font-normal">{t('orienteering.overdistance')}</th>
                </>
              )}
              {still && <th className="py-1 font-normal">{t('orienteering.still')}</th>}
            </tr>
          </thead>
          <tbody>
            {legs.map((leg) => (
              <tr key={leg.number}>
                <td className="py-1.5 pr-3">
                  {leg.number}
                  {leg.miss && (
                    <span className="ml-2 rounded-full border border-amber-500/50 bg-amber-500/10 px-2 py-0.5 text-xs">
                      {t('orienteering.miss')}
                    </span>
                  )}
                </td>
                <td className="py-1.5 pr-3">{formatDuration(leg.durationS)}</td>
                <td className="py-1.5 pr-3">{number.format(leg.distanceM)} m</td>
                {straight && (
                  <>
                    <td className="py-1.5 pr-3">
                      {leg.straightM === null ? '–' : `${number.format(leg.straightM)} m`}
                    </td>
                    <td className="py-1.5 pr-3">
                      {leg.straightM ? percent.format(leg.distanceM / leg.straightM - 1) : '–'}
                    </td>
                  </>
                )}
                {still && (
                  <td className="py-1.5">
                    {leg.stillS === null ? '–' : formatDuration(leg.stillS)}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

type CourseRunner = {
  activityId: string
  userId: string
  displayName: string
  avatarId: string | null
  sport: string
  title: string | null
  startedAt: string
  durationS: number
  distanceM: number
  courseLengthM: number | null
  attribution: string | null
  legs: { durationS: number; distanceM: number }[]
}

// "I ran the same course": legs side by side for everyone linked to the course whose activity the viewer may read.
export function SameCourse({ activity, meId }: { activity: ActivityDetail; meId: string }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const returnTo = useRouterState({ select: (s) => s.location.href })
  const runners = useQuery({
    queryKey: ['activities', activity.id, 'same-course'],
    queryFn: () => api.get<CourseRunner[]>(`/api/activities/${activity.id}/same-course`),
  })
  const own = activity.userId === meId
  const unlink = useMutation({
    mutationFn: () =>
      api.send('PUT', `/api/activities/${activity.id}/same-course`, { activityId: null }),
    onSuccess: () => refreshActivityQueries(queryClient),
  })
  const data = runners.data ?? []
  const legCount = Math.max(0, ...data.map((r) => r.legs.length))
  const sameLegCount = data.every((r) => r.legs.length === legCount)
  const best = (values: (number | undefined)[]) =>
    Math.min(...values.filter((v): v is number => v !== undefined))
  return (
    <div className="mt-6">
      {data.length > 0 && (
        <section className={card()} aria-labelledby="same-course-title">
          <Heading level={2} id="same-course-title">
            {t('orienteering.sameCourse')}
          </Heading>
          {!sameLegCount && (
            <p className="mt-2 text-sm text-muted-foreground">
              {t('orienteering.differentLegCount')}
            </p>
          )}
          <div className="mt-3 overflow-x-auto">
            <table className="w-full text-sm whitespace-nowrap tabular-nums">
              <thead className="text-left align-bottom text-xs text-muted-foreground">
                <tr>
                  <th className="py-1 pr-3 font-normal">{t('orienteering.leg')}</th>
                  {data.map((r) => (
                    <th key={r.activityId} className="py-1 pr-3 font-normal">
                      <span className="flex items-center gap-2">
                        <Avatar name={r.displayName} avatarId={r.avatarId} size="sm" />
                        {r.activityId === activity.id ? (
                          <span className="text-foreground">{r.displayName}</span>
                        ) : (
                          <Link
                            to="/app/activities/$activityId"
                            params={{ activityId: r.activityId }}
                            search={{ returnTo }}
                            className="text-foreground underline underline-offset-4"
                          >
                            {r.displayName}
                          </Link>
                        )}
                      </span>
                      {r.attribution && <span className="mt-1 block">{r.attribution}</span>}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {Array.from({ length: legCount }, (_, i) => {
                  const fastest = best(data.map((r) => r.legs[i]?.durationS))
                  return (
                    <tr key={i}>
                      <td className="py-1.5 pr-3">{i + 1}</td>
                      {data.map((r) => {
                        const leg = r.legs[i]
                        return (
                          <td
                            key={r.activityId}
                            className={`py-1.5 pr-3 ${leg?.durationS === fastest ? 'font-semibold' : ''}`}
                          >
                            {leg ? formatDuration(leg.durationS) : '–'}
                          </td>
                        )
                      })}
                    </tr>
                  )
                })}
                <tr>
                  <td className="py-1.5 pr-3 font-medium">{t('orienteering.total')}</td>
                  {data.map((r) => (
                    <td key={r.activityId} className="py-1.5 pr-3 font-medium">
                      {formatDuration(r.durationS)}
                    </td>
                  ))}
                </tr>
              </tbody>
            </table>
          </div>
          {own && activity.sameCourseAs && (
            <>
              <Button
                variant="link-muted"
                className="mt-3"
                onClick={() => unlink.mutate()}
                disabled={unlink.isPending}
              >
                {t('orienteering.unlink')}
              </Button>
              <ErrorText error={unlink.error} />
            </>
          )}
        </section>
      )}
      <QueryError query={runners} />
      {!own && runners.isSuccess && !data.some((r) => r.userId === meId) && (
        <LinkSameCourse activity={activity} />
      )}
    </div>
  )
}

// Picks one of the viewer's own orienteering activities, the one closest in time first, and links it to this one.
function LinkSameCourse({ activity }: { activity: ActivityDetail }) {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const dialog = useRef<HTMLDialogElement>(null)
  const [open, setOpen] = useState(false)
  const [selected, setSelected] = useState<string | null>(null)
  const mine = useQuery({
    queryKey: ['activities', 'orienteering'],
    queryFn: () => api.get<ActivityPage>('/api/activities?sport=orienteering'),
    enabled: open,
  })
  const start = new Date(activity.startedAt).getTime()
  const options = [...(mine.data?.items ?? [])].sort(
    (a, b) =>
      Math.abs(new Date(a.startedAt).getTime() - start) -
      Math.abs(new Date(b.startedAt).getTime() - start),
  )
  const choice = selected ?? options[0]?.id ?? null
  const link = useMutation({
    mutationFn: (id: string) =>
      api.send('PUT', `/api/activities/${id}/same-course`, { activityId: activity.id }),
    onSuccess: async () => {
      dialog.current?.close()
      await refreshActivityQueries(queryClient)
    },
  })
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })
  return (
    <>
      <Button
        variant="secondary-outline"
        className="mt-3"
        onClick={() => {
          setOpen(true)
          dialog.current?.showModal()
        }}
      >
        {t('orienteering.ranSame')}
      </Button>
      <Modal
        ref={dialog}
        aria-labelledby="same-course-modal-title"
        onClose={() => setOpen(false)}
        onCancel={(event) => link.isPending && event.preventDefault()}
      >
        <Heading level={3} id="same-course-modal-title">
          {t('orienteering.ranSame')}
        </Heading>
        <p className="mt-3 text-sm text-muted-foreground">{t('orienteering.ranSameHelp')}</p>
        <form
          className="mt-4 space-y-4"
          onSubmit={(event) => {
            event.preventDefault()
            if (choice && !link.isPending) link.mutate(choice)
          }}
        >
          {mine.isSuccess && options.length === 0 ? (
            <p className="text-sm">{t('orienteering.noOwnActivities')}</p>
          ) : (
            <FormField label={t('orienteering.ownActivity')}>
              {(id) => (
                <Select
                  id={id}
                  value={choice ?? ''}
                  disabled={!mine.isSuccess || link.isPending}
                  onChange={(event) => setSelected(event.currentTarget.value)}
                >
                  {options.map((a) => (
                    <option key={a.id} value={a.id}>
                      {`${date.format(new Date(a.startedAt))} · ${activityTitle(a, t)}`}
                    </option>
                  ))}
                </Select>
              )}
            </FormField>
          )}
          <QueryError query={mine} />
          <ErrorText error={link.error} />
          <div className="flex justify-end gap-3">
            <Button
              type="button"
              variant="secondary-outline"
              disabled={link.isPending}
              onClick={() => dialog.current?.close()}
            >
              {t('activity.cancel')}
            </Button>
            <Button type="submit" disabled={!choice || link.isPending}>
              {t('orienteering.link')}
            </Button>
          </div>
        </form>
      </Modal>
    </>
  )
}
