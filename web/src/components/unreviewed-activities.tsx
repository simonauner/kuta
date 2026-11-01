import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Sparkles } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Reveal } from '@/components/reveal'
import { Button } from '@/components/ui/button'
import { refreshActivityQueries } from '@/lib/activity-query'
import { activityTitle } from '@/lib/activity'
import { api } from '@/lib/api'

type Unreviewed = {
  count: number
  latest: { id: string; title: string | null; sport: string; startedAt: string } | null
}

// Garmin imports arrive with Garmin's name and no description; suggest editing them without forcing it.
export function UnreviewedActivities({ className }: { className?: string }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const unreviewed = useQuery({
    queryKey: ['activities', 'unreviewed'],
    queryFn: () => api.get<Unreviewed>('/api/activities/unreviewed'),
  })
  const dismiss = useMutation({
    mutationFn: () => api.send('POST', '/api/activities/unreviewed/dismiss'),
    onSuccess: () => refreshActivityQueries(queryClient),
  })
  const latest = unreviewed.data?.latest
  if (!unreviewed.data?.count || !latest) return null
  const single = unreviewed.data.count === 1
  return (
    <Reveal>
      {/* Spacing goes on a padded wrapper: a margin inside Reveal collapses out of its animated height. */}
      <div className={className}>
        <div role="status" className="flex gap-3 rounded-xl border bg-card p-4 text-sm">
          <Sparkles className="mt-0.5 size-5 shrink-0 text-primary" />
          <div>
            {single
              ? t('unreviewed.titleSingle', { title: activityTitle(latest, t) })
              : t('unreviewed.title', { count: unreviewed.data.count })}
            <p className="mt-1 text-muted-foreground">{t('unreviewed.text')}</p>
            <div className="mt-3 flex flex-wrap gap-2">
              <Button asChild size="sm">
                <Link to="/app/activities/$activityId/edit" params={{ activityId: latest.id }}>
                  {single ? t('unreviewed.editSingle') : t('unreviewed.edit')}
                </Link>
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={dismiss.isPending}
                onClick={() => dismiss.mutate()}
              >
                {single ? t('unreviewed.dismissSingle') : t('unreviewed.dismiss')}
              </Button>
            </div>
            {dismiss.error && (
              <p role="alert" className="mt-2 text-destructive">
                <ErrorMessage error={dismiss.error} />
              </p>
            )}
          </div>
        </div>
      </div>
    </Reveal>
  )
}
