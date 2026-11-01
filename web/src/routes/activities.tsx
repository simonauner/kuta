import { FormField } from '@/components/ui/form-field'
import { SportIcon } from '@/components/sport-icon'
import { sports } from '@/lib/sports'
import { UnreviewedActivities } from '@/components/unreviewed-activities'
import { TagLabels } from '@/components/activity-tags'
import { activityTagsQuery, activityYearsQuery, refreshActivityQueries } from '@/lib/activity-query'
import { ErrorMessage } from '@/components/error-message'
import { ActivitiesEmptyState } from '@/components/activities-empty-state'
import { SlowLoading } from '@/components/slow-loading'
import { GoalDialog, GoalList } from '@/components/goals'
import { TrainingLoadWarning } from '@/components/training-load'
import type { Goal } from '@/lib/goals'
import { useState } from 'react'
import { motion, useReducedMotion } from 'motion/react'
import { Reveal } from '@/components/reveal'
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
  const { year, tag, sport } = route.useSearch()
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
  const reduceMotion = useReducedMotion()
  const tagOptions = useQuery(activityTagsQuery)
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const years = useQuery(activityYearsQuery(timeZone))
  const availableYears = years.data ?? []
  const activities = useInfiniteQuery({
    queryKey: ['activities', 'list', timeZone, year, tag, sport],
    initialPageParam: null as ActivityCursor | null,
    queryFn: ({ pageParam }) => {
      const params = new URLSearchParams({ timeZone })
      if (year !== undefined) params.set('year', String(year))
      if (tag) params.set('tag', tag)
      if (sport) params.set('sport', sport)
      if (pageParam) {
        params.set('beforeStartedAt', pageParam.startedAt)
        params.set('beforeId', pageParam.id)
      }
      return api.get<ActivityPage>(`/api/activities?${params}`)
    },
    getNextPageParam: (page) => page.nextCursor,
  })
  const items = activities.data?.pages.flatMap((page) => page.items) ?? []
  const showOnboarding =
    years.isSuccess &&
    availableYears.length === 0 &&
    activities.isSuccess &&
    items.length === 0 &&
    year === undefined &&
    !tag &&
    !sport
  // null = closed, 'new' = new goal, otherwise the goal being edited
  const [goalForm, setGoalForm] = useState<Goal | 'new' | null>(null)

  const remove = useMutation({
    mutationFn: (id: string) => api.send('DELETE', `/api/activities/${id}`),
    onSuccess: () => refreshActivityQueries(queryClient),
  })

  return (
    <section>
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-2xl font-semibold tracking-tight">{t('nav.activities')}</h1>
        <div className="flex flex-wrap justify-end gap-2">
          <Button size="sm" variant="outline" onClick={() => setGoalForm('new')}>
            {t('goals.new')}
          </Button>
          {!showOnboarding && (
            <Button asChild size="sm">
              <Link to="/app/activities/new" search={{ year, tag, sport }}>
                {t('newActivity.title')}
              </Link>
            </Button>
          )}
        </div>
      </div>
      <TrainingLoadWarning />
      <UnreviewedActivities className="pt-4" />
      <GoalList onSelect={setGoalForm} />
      {goalForm && (
        <GoalDialog
          key={goalForm === 'new' ? 'new' : goalForm.id}
          goal={goalForm === 'new' ? null : goalForm}
          onClose={() => setGoalForm(null)}
        />
      )}
      {!showOnboarding && (availableYears.length > 1 || year !== undefined) && (
        <Reveal>
          <FormField layout="inline" label={t('activities.year')} className="mt-6">
            {(id) => (
              <select
                id={id}
                className="w-32 rounded-md border bg-background px-3 py-2 text-sm text-foreground"
                value={year ?? ''}
                onChange={(e) =>
                  void navigate({
                    search: {
                      year: e.target.value ? Number(e.target.value) : undefined,
                      tag,
                      sport,
                    },
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
            )}
          </FormField>
        </Reveal>
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
      {!showOnboarding && (
        <>
          <FormField layout="inline" label={t('tags.label')} className="mt-5 text-sm">
            {(id) => (
              <select
                id={id}
                className="w-44 rounded-md border bg-background px-3 py-2"
                value={tag ?? ''}
                onChange={(e) =>
                  void navigate({
                    search: activitySearch({ year, tag: e.currentTarget.value, sport }),
                  })
                }
              >
                <option value="">{t('tags.all')}</option>
                {tagOptions.data?.map((value) => (
                  <option key={value} value={value}>
                    {t(`tagNames.${value}`)}
                  </option>
                ))}
              </select>
            )}
          </FormField>
          <FormField layout="inline" label={t('activities.sport')} className="mt-5 text-sm">
            {(id) => (
              <select
                id={id}
                className="w-52 rounded-md border bg-background px-3 py-2"
                value={sport ?? ''}
                onChange={(e) =>
                  void navigate({
                    search: activitySearch({ year, tag, sport: e.currentTarget.value }),
                  })
                }
              >
                <option value="">{t('activities.allSports')}</option>
                {sports.map((value) => (
                  <option key={value} value={value}>
                    {t(`newActivity.sports.${value}`)}
                  </option>
                ))}
              </select>
            )}
          </FormField>
        </>
      )}
      {remove.error && (
        <p role="alert" className="mt-4 text-sm text-destructive">
          <ErrorMessage error={remove.error} />
        </p>
      )}

      <SlowLoading loading={activities.isPending}>
        {activities.isSuccess &&
          items.length === 0 &&
          (showOnboarding ? (
            <ActivitiesEmptyState context="activities" />
          ) : (
            <p className="mt-8 text-muted-foreground">
              {t(year !== undefined || tag || sport ? 'activities.noMatches' : 'activities.empty')}
            </p>
          ))}
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
                  <motion.li
                    key={a.id}
                    className="flex items-start justify-between gap-4 py-4"
                    initial={reduceMotion ? false : { opacity: 0, y: 8 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.2, ease: 'easeOut' }}
                  >
                    <div>
                      <Link
                        to="/app/activities/$activityId"
                        params={{ activityId: a.id }}
                        search={{ year, tag, sport }}
                        className="inline-flex items-center gap-2 font-medium underline-offset-2 hover:underline"
                      >
                        <span
                          className="inline-block"
                          style={{ viewTransitionName: `activity-title-${a.id}` }}
                        >
                          {activityTitle(a, t)}
                        </span>
                        <SportIcon
                          sport={a.sport}
                          style={{ viewTransitionName: `activity-sport-${a.id}` }}
                        />
                      </Link>
                      {a.unreviewed && (
                        <span className="mr-2 rounded-full bg-primary/15 px-2 py-0.5 text-xs font-medium text-primary">
                          {t('unreviewed.label')}
                        </span>
                      )}
                      {a.attribution && (
                        <p className="text-xs text-muted-foreground">{a.attribution}</p>
                      )}
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
                  </motion.li>
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
