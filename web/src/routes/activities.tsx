import { ErrorMessage } from '@/components/error-message'
import { useTranslation } from 'react-i18next'
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import {
  activityTitle,
  formatDuration,
  formatPace,
  groupActivities,
  type ActivityCursor,
  type ActivityPage,
} from '@/lib/activity'

export function ActivitiesPage() {
  const { t, i18n } = useTranslation()
  const dateFormat = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
  const monthFormat = new Intl.DateTimeFormat(i18n.resolvedLanguage, { month: 'long' })
  const kmFormat = new Intl.NumberFormat(i18n.resolvedLanguage, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
  const queryClient = useQueryClient()
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const activities = useInfiniteQuery({
    queryKey: ['activities', timeZone],
    initialPageParam: null as ActivityCursor | null,
    queryFn: ({ pageParam }) => {
      const params = new URLSearchParams({ timeZone })
      if (pageParam) {
        params.set('beforeStartedAt', pageParam.startedAt)
        params.set('beforeId', pageParam.id)
      }
      return api.get<ActivityPage>(`/api/activities?${params}`)
    },
    getNextPageParam: (page) => page.nextCursor,
  })
  const items = activities.data?.pages.flatMap((page) => page.items) ?? []

  const remove = useMutation({
    mutationFn: (id: string) => api.send('DELETE', `/api/activities/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['activities'] }),
  })

  return (
    <section>
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-2xl font-semibold tracking-tight">{t('nav.activities')}</h1>
        <Button asChild size="sm">
          <Link to="/app/activities/new">{t('newActivity.title')}</Link>
        </Button>
      </div>
      {remove.error && (
        <p role="alert" className="mt-4 text-sm text-destructive">
          <ErrorMessage error={remove.error} />
        </p>
      )}

      {activities.isSuccess && items.length === 0 && (
        <p className="mt-8 text-muted-foreground">{t('activities.empty')}</p>
      )}
      {activities.isPending && (
        <p role="status" className="mt-8 text-muted-foreground">
          {t('activities.loading')}
        </p>
      )}
      {activities.error && (
        <p role="alert" className="mt-4 text-sm text-destructive">
          <ErrorMessage error={activities.error} />
        </p>
      )}
      <div className="mt-8 space-y-6">
        {groupActivities(items).map((period) => (
          <div key={period.activities[0].id}>
            {period.year !== null && (
              <h2 className="mb-4 text-xl font-semibold tracking-tight">{period.year}</h2>
            )}
            {period.month !== null && (
              <h3 className="mb-3 text-lg font-medium">{monthFormat.format(period.month)}</h3>
            )}
            {period.week !== null && (
              <h4 className="text-sm font-medium text-muted-foreground">
                {t('activities.week', { week: period.week })}
              </h4>
            )}
            <ul className="divide-y">
              {period.activities.map((a) => (
                <li key={a.id} className="flex items-start justify-between gap-4 py-4">
                  <div>
                    <Link
                      to="/app/activities/$activityId"
                      params={{ activityId: a.id }}
                      className="inline-block font-medium underline-offset-2 hover:underline"
                      style={{ viewTransitionName: `activity-title-${a.id}` }}
                    >
                      {activityTitle(a, t)}
                    </Link>
                    <p className="text-xs text-muted-foreground">
                      {dateFormat.format(new Date(a.startedAt))}
                    </p>
                    <p className="mt-1 text-sm text-muted-foreground">
                      {kmFormat.format(a.distanceM / 1000)} km · {formatDuration(a.durationS)} ·{' '}
                      {formatPace(a.durationS, a.distanceM)}
                      {a.avgHr !== null && ` · ${a.avgHr} bpm`}
                    </p>
                  </div>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="text-muted-foreground"
                    disabled={remove.isPending}
                    aria-label={t('activities.deleteLabel', {
                      date: dateFormat.format(new Date(a.startedAt)),
                    })}
                    onClick={() => {
                      if (window.confirm(t('activities.confirmDelete'))) remove.mutate(a.id)
                    }}
                  >
                    {t('activities.delete')}
                  </Button>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
      {(activities.hasNextPage || (activities.isError && !activities.data)) && (
        <Button
          variant="outline"
          className="mt-6"
          disabled={activities.isFetching}
          onClick={() => {
            if (activities.data) void activities.fetchNextPage()
            else void activities.refetch()
          }}
        >
          {activities.isFetching
            ? t('activities.loading')
            : activities.isError
              ? t('activities.retry')
              : t('activities.loadOlder')}
        </Button>
      )}
    </section>
  )
}
