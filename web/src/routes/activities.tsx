import { ErrorMessage } from '@/components/error-message'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import { activityTitle, formatDuration, formatPace, type Activity } from '@/lib/activity'

const activitiesQuery = {
  queryKey: ['activities'],
  queryFn: () => api.get<Activity[]>('/api/activities'),
}

export function ActivitiesPage() {
  const { t, i18n } = useTranslation()
  const dateFormat = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
  const kmFormat = new Intl.NumberFormat(i18n.resolvedLanguage, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
  const queryClient = useQueryClient()
  const activities = useQuery(activitiesQuery)

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

      {activities.data?.length === 0 && (
        <p className="mt-8 text-muted-foreground">{t('activities.empty')}</p>
      )}
      <ul className="mt-8 divide-y">
        {activities.data?.map((a) => (
          <li key={a.id} className="flex items-start justify-between gap-4 py-4">
            <div>
              <Link
                to="/app/activities/$activityId"
                params={{ activityId: a.id }}
                className="font-medium underline-offset-2 hover:underline"
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
    </section>
  )
}
