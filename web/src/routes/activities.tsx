import { TagLabels } from '@/components/activity-tags'
import { equipmentQuery, lastUsedShoeQuery } from '@/lib/equipment'
import { activityTagsQuery } from '@/lib/activity-query'
import { ErrorMessage } from '@/components/error-message'
import { SlowLoading } from '@/components/slow-loading'
import { useTranslation } from 'react-i18next'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { getRouteApi, Link } from '@tanstack/react-router'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import {
  activityTitle,
  activitySearch,
  formatDuration,
  formatSportPace,
  groupActivities,
  type ActivityCursor,
  type ActivityPage,
} from '@/lib/activity'

const route = getRouteApi('/app/activities')

export function ActivitiesPage() {
  const { t, i18n } = useTranslation()
  const { year, tag } = route.useSearch()
  const navigate = route.useNavigate()
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
  const tagOptions = useQuery(activityTagsQuery)
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const years = useQuery({
    queryKey: ['activities', 'years', timeZone],
    queryFn: () => api.get<number[]>(`/api/activities/years?${new URLSearchParams({ timeZone })}`),
    staleTime: 60_000,
  })
  const availableYears = years.data ?? []
  const activities = useInfiniteQuery({
    queryKey: ['activities', 'list', timeZone, year, tag],
    initialPageParam: null as ActivityCursor | null,
    queryFn: ({ pageParam }) => {
      const params = new URLSearchParams({ timeZone })
      if (year !== undefined) params.set('year', String(year))
      if (tag) params.set('tag', tag)
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
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: ['activities'] }),
        queryClient.invalidateQueries({ queryKey: equipmentQuery.queryKey }),
        queryClient.invalidateQueries({ queryKey: lastUsedShoeQuery.queryKey }),
      ]),
  })

  return (
    <section>
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-2xl font-semibold tracking-tight">{t('nav.activities')}</h1>
        <Button asChild size="sm">
          <Link to="/app/activities/new" search={{ year, tag }}>
            {t('newActivity.title')}
          </Link>
        </Button>
      </div>
      {(availableYears.length > 1 || year !== undefined) && (
        <div className="mt-6 flex items-center gap-3">
          <label htmlFor="activity-year" className="text-sm font-medium">
            {t('activities.year')}
          </label>
          <select
            id="activity-year"
            className="w-32 rounded-md border bg-background px-3 py-2 text-sm text-foreground"
            value={year ?? ''}
            onChange={(e) =>
              void navigate({
                search: { year: e.target.value ? Number(e.target.value) : undefined, tag },
              })
            }
          >
            <option value="">{t('activities.allYears')}</option>
            {year !== undefined && !availableYears.includes(year) && (
              <option value={year}>{year}</option>
            )}
            {availableYears.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
        </div>
      )}
      {years.error && (
        <div role="alert" className="mt-4 text-sm text-destructive">
          <ErrorMessage error={years.error} />{' '}
          <Button
            variant="outline"
            size="sm"
            disabled={years.isFetching}
            onClick={() => void years.refetch()}
          >
            {t('activities.retry')}
          </Button>
        </div>
      )}
      <label className="mt-5 flex items-center gap-3 text-sm">
        {t('tags.label')}
        <select
          className="w-44 rounded-md border bg-background px-3 py-2"
          value={tag ?? ''}
          onChange={(e) =>
            void navigate({ search: activitySearch({ year, tag: e.currentTarget.value }) })
          }
        >
          <option value="">{t('tags.all')}</option>
          {tagOptions.data?.map((value) => (
            <option key={value} value={value}>
              {t(`tagNames.${value}`)}
            </option>
          ))}
        </select>
      </label>
      {remove.error && (
        <p role="alert" className="mt-4 text-sm text-destructive">
          <ErrorMessage error={remove.error} />
        </p>
      )}

      <SlowLoading loading={activities.isPending}>
        {activities.isSuccess && items.length === 0 && (
          <p className="mt-8 text-muted-foreground">{t(tag ? 'tags.empty' : 'activities.empty')}</p>
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
                        search={{ year, tag }}
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
                        {formatSportPace(a.sport, a.durationS, a.distanceM, i18n.resolvedLanguage)}
                        {a.avgHr !== null && ` · ${a.avgHr} bpm`}
                      </p>
                      <TagLabels tags={a.tags} />
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
        <SlowLoading loading={activities.isFetchingNextPage}>
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
              {activities.isError ? t('activities.retry') : t('activities.loadOlder')}
            </Button>
          )}
        </SlowLoading>
      </SlowLoading>
    </section>
  )
}
