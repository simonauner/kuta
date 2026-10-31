import type { ReactNode } from 'react'
import { FeedEmptyState } from '@/components/feed-empty-state'
import { ActivityMapPreview } from '@/components/activity-map-preview'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { SportIcon } from '@/components/sport-icon'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import { activityTitle, formatDuration, type ActivityCursor } from '@/lib/activity'

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
}
type FeedPage = { items: FeedItem[]; nextCursor: ActivityCursor | null }

export function ActivityFeed({
  runnerId,
  emptyState,
}: {
  runnerId?: string
  emptyState?: ReactNode
}) {
  const { t, i18n } = useTranslation()
  const feed = useInfiniteQuery({
    queryKey: runnerId ? ['feed', runnerId] : ['feed'],
    initialPageParam: null as ActivityCursor | null,
    queryFn: ({ pageParam }) => {
      const params = new URLSearchParams()
      if (pageParam) {
        params.set('beforeStartedAt', pageParam.startedAt)
        params.set('beforeId', pageParam.id)
      }
      return api.get<FeedPage>(
        `${runnerId ? `/api/runners/${runnerId}/activities` : '/api/feed'}?${params}`,
      )
    },
    getNextPageParam: (page) => page.nextCursor,
  })
  const number = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 2 })
  const when = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
  return (
    <>
      {feed.isPending && <p role="status">{t('social.loading')}</p>}
      {feed.error && (
        <div role="alert">
          <ErrorMessage error={feed.error} />
          <Button variant="outline" onClick={() => void feed.refetch()}>
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
      <ul className="space-y-4">
        {feed.data?.pages
          .flatMap((page) => page.items)
          .map((activity) => (
            <li key={activity.id} className="rounded-lg border p-4">
              <div className="flex items-center justify-between gap-3 text-sm text-muted-foreground">
                <Link
                  to="/app/runners/$runnerId"
                  params={{ runnerId: activity.userId }}
                  className="break-words font-medium text-foreground underline-offset-4 hover:underline"
                >
                  {activity.displayName}
                </Link>
              </div>
              <Link
                to="/app/activities/$activityId"
                params={{ activityId: activity.id }}
                className="mt-3 flex items-center gap-3 font-semibold underline-offset-4 hover:underline"
              >
                <SportIcon sport={activity.sport} className="size-6 shrink-0" />
                <span className="break-words">{activityTitle(activity, t)}</span>
              </Link>
              <p className="mt-2 text-sm text-muted-foreground">
                {when.format(new Date(activity.startedAt))}
              </p>
              <p className="mt-3 text-sm">
                {number.format(activity.distanceM / 1000)} km · {formatDuration(activity.durationS)}
              </p>
              {activity.routeGeoJson && <ActivityMapPreview geoJson={activity.routeGeoJson} />}
            </li>
          ))}
      </ul>
      {feed.hasNextPage && (
        <Button
          variant="outline"
          disabled={feed.isFetchingNextPage}
          onClick={() => void feed.fetchNextPage()}
        >
          {t('activities.loadOlder')}
        </Button>
      )}
    </>
  )
}
