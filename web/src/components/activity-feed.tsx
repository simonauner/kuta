import { SlowLoading } from '@/components/slow-loading'
import type { ReactNode } from 'react'
import { FeedEmptyState } from '@/components/feed-empty-state'
import { ActivityMapPreview } from '@/components/activity-map-preview'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Link, useRouterState } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { SportIcon } from '@/components/sport-icon'
import { Avatar } from '@/components/avatar'
import { SupporterBadge } from '@/components/supporter'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import {
  activityTitle,
  formatDuration,
  formatSportPace,
  relativeDay,
  type ActivityCursor,
} from '@/lib/activity'
import { isCyclingSport } from '@/lib/sports'
import { RecordMedal } from '@/components/record-award'
import type { RecordDistance } from '@/lib/records'
import type { SupporterTier } from '@/lib/auth'
import { card } from '@/components/ui/card-variants'

type FeedItem = {
  id: string
  userId: string
  displayName: string
  sport: string
  title: string | null
  startedAt: string
  durationS: number
  distanceM: number
  routeGeoJson: string | null
  supporterTier: SupporterTier | null
  attribution: string | null
  avatarId: string | null
  movingS: number | null
  // The most notable record, only on the viewer's own activities
  recordStatus: 'current' | 'previous' | 'year' | null
  recordDistanceM: RecordDistance | null
  recordElapsedS: number | null
  recordYear: number | null
  recordCount: number
}
type FeedPage = { items: FeedItem[]; nextCursor: ActivityCursor | null }

export function ActivityFeed({
  runnerId,
  viewAs,
  emptyState,
  visitor = false,
  limit,
}: {
  runnerId?: string
  viewAs?: 'followers' | 'public'
  emptyState?: ReactNode
  // Without an account: the runner's public activities through the public API
  visitor?: boolean
  // Show only the latest activities, without loading older ones.
  limit?: number
}) {
  const { t, i18n } = useTranslation()
  const returnTo = useRouterState({ select: (s) => s.location.href })
  const feed = useInfiniteQuery({
    queryKey: runnerId ? ['feed', runnerId, visitor, ...(viewAs ? [viewAs] : [])] : ['feed'],
    initialPageParam: null as ActivityCursor | null,
    queryFn: ({ pageParam }) => {
      const params = new URLSearchParams()
      if (viewAs) params.set('viewAs', viewAs)
      if (pageParam) {
        params.set('beforeStartedAt', pageParam.startedAt)
        params.set('beforeId', pageParam.id)
      }
      return api.get<FeedPage>(
        `${runnerId ? `/api/${visitor ? 'public/' : ''}runners/${runnerId}/activities` : '/api/feed'}?${params}`,
      )
    },
    getNextPageParam: (page) => page.nextCursor,
  })
  const number = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 2 })
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
  const time = new Intl.DateTimeFormat(i18n.resolvedLanguage, { timeStyle: 'short' })
  const now = new Date()
  const when = (startedAt: string) => {
    const start = new Date(startedAt)
    const day = relativeDay(start, now)
    return day ? t(`feed.${day}`, { time: time.format(start) }) : date.format(start)
  }
  const metrics = (activity: FeedItem) => {
    const pace = formatSportPace(
      activity.sport,
      activity.movingS ?? activity.durationS,
      activity.distanceM,
      i18n.resolvedLanguage,
    )
    return [
      activity.distanceM >= 1 && [
        t('activity.distance'),
        `${number.format(activity.distanceM / 1000)} km`,
      ],
      pace !== '–' && [
        t(isCyclingSport(activity.sport) ? 'activity.speed' : 'activity.pace'),
        pace,
      ],
      [t('activity.time'), formatDuration(activity.durationS)],
    ].filter((m): m is [string, string] => !!m)
  }
  return (
    <SlowLoading loading={feed.isPending}>
      {feed.error && (
        <div role="alert">
          <ErrorMessage error={feed.error} />
          <Button variant="secondary-outline" onClick={() => void feed.refetch()}>
            {t('activities.retry')}
          </Button>
        </div>
      )}
      {feed.isSuccess &&
        feed.data.pages[0].items.length === 0 &&
        (emptyState ??
          (runnerId ? (
            <p className="text-muted-foreground">{t('social.noActivities')}</p>
          ) : (
            <FeedEmptyState />
          )))}
      <ul className="space-y-2 sm:space-y-4">
        {feed.data?.pages
          .flatMap((page) => page.items)
          .slice(0, limit)
          .map((activity) => (
            <li key={activity.id} className={card({ bleed: true, className: 'overflow-hidden' })}>
              <div className="flex items-center gap-3">
                <Avatar name={activity.displayName} avatarId={activity.avatarId} size="md" />
                <div className="min-w-0">
                  <div className="flex items-center gap-1.5">
                    <Link
                      to="/app/runners/$runnerId"
                      params={{ runnerId: activity.userId }}
                      className="break-words font-medium underline-offset-4 underline"
                    >
                      {activity.displayName}
                    </Link>
                    <SupporterBadge tier={activity.supporterTier} />
                  </div>
                  <p className="text-metadata text-muted-foreground">
                    <time dateTime={activity.startedAt}>{when(activity.startedAt)}</time>
                  </p>
                </div>
              </div>
              <Link
                to="/app/activities/$activityId"
                params={{ activityId: activity.id }}
                search={{ returnTo }}
                className="mt-4 flex items-center gap-3 text-xl font-semibold underline-offset-4 underline"
              >
                <SportIcon sport={activity.sport} className="size-6 shrink-0" />
                <span className="break-words">{activityTitle(activity, t)}</span>
              </Link>
              {activity.attribution && (
                <p className="mt-1 text-metadata text-muted-foreground">{activity.attribution}</p>
              )}
              <dl className="mt-4 flex flex-wrap gap-x-8 gap-y-3">
                {metrics(activity).map(([label, value]) => (
                  <div key={label}>
                    <dt className="text-metadata text-muted-foreground">{label}</dt>
                    <dd className="text-xl font-semibold tabular-nums">{value}</dd>
                  </div>
                ))}
              </dl>
              {activity.recordStatus && (
                <div className="mt-4 flex items-center gap-3 rounded-lg bg-muted px-3 py-2">
                  <RecordMedal status={activity.recordStatus} />
                  <div className="min-w-0">
                    <p className="font-semibold">
                      {t(`feed.record.${activity.recordStatus}`, {
                        distance: t(`records.distances.${activity.recordDistanceM!}`),
                        year: activity.recordYear,
                      })}
                    </p>
                    <p className="text-sm text-muted-foreground tabular-nums">
                      {formatDuration(activity.recordElapsedS!)}
                      {activity.recordCount > 1 &&
                        ` · ${t('feed.moreRecords', { count: activity.recordCount - 1 })}`}
                    </p>
                  </div>
                </div>
              )}
              {activity.routeGeoJson && <ActivityMapPreview geoJson={activity.routeGeoJson} />}
            </li>
          ))}
      </ul>
      {feed.hasNextPage && !limit && (
        <Button
          variant="secondary-outline"
          disabled={feed.isFetchingNextPage}
          onClick={() => void feed.fetchNextPage()}
        >
          {t('activities.loadOlder')}
        </Button>
      )}
    </SlowLoading>
  )
}
