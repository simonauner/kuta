import { SlowLoading } from '@/components/slow-loading'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { ActivityFeed } from '@/components/activity-feed'
import { ActivityCalendar, ProfileStats } from '@/components/runner-summary'
import { GoalList } from '@/components/goals'
import { SupporterBadge } from '@/components/supporter'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { ApiError, api } from '@/lib/api'
import { ProfilePage } from '@/routes/profile'
import { meQuery, type SupporterTier } from '@/lib/auth'

type Runner = {
  id: string
  displayName: string
  status: 'none' | 'pending' | 'accepted'
  supporterTier: SupporterTier | null
}

export function RunnerPage() {
  const { runnerId } = useParams({ from: '/app/runners/$runnerId' })
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const { data: me } = useQuery(meQuery)
  const runner = useQuery({
    queryKey: ['runner', runnerId],
    queryFn: () => api.get<Runner>(`/api/runners/${runnerId}`),
    retry: (count, error) => !(error instanceof ApiError && error.status === 404) && count < 1,
  })
  const follow = useMutation({
    mutationFn: () =>
      api.send(runner.data?.status === 'none' ? 'POST' : 'DELETE', `/api/following/${runnerId}`),
    onSuccess: async () => {
      await Promise.all(
        ['runner', 'follows', 'feed', 'activity'].map((key) =>
          queryClient.invalidateQueries({ queryKey: [key] }),
        ),
      )
    },
  })
  if (me?.id === runnerId) return <ProfilePage />
  return (
    <section className="space-y-6">
      <Link to="/app" className="text-sm text-muted-foreground underline-offset-2 hover:underline">
        ← {t('nav.feed')}
      </Link>
      <SlowLoading loading={runner.isPending}>
        {runner.error && (
          <div role="alert">
            {runner.error instanceof ApiError && runner.error.status === 404 ? (
              t('social.runnerNotFound')
            ) : (
              <>
                <ErrorMessage error={runner.error} />
                <Button variant="outline" onClick={() => void runner.refetch()}>
                  {t('activities.retry')}
                </Button>
              </>
            )}
          </div>
        )}
        {runner.data && !runner.isError && (
          <>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h1 className="flex min-w-0 items-center gap-2 break-words text-2xl font-semibold tracking-tight">
                {runner.data.displayName}
                <SupporterBadge tier={runner.data.supporterTier} />
              </h1>
              {me?.id !== runnerId && (
                <div className="flex flex-wrap items-center gap-3">
                  {runner.data.status === 'pending' && (
                    <span className="text-sm text-muted-foreground">{t('social.pending')}</span>
                  )}
                  <Button
                    variant="outline"
                    disabled={follow.isPending}
                    onClick={() => follow.mutate()}
                  >
                    {t(
                      runner.data.status === 'accepted'
                        ? 'social.unfollow'
                        : runner.data.status === 'pending'
                          ? 'social.cancel'
                          : 'social.follow',
                    )}
                  </Button>
                </div>
              )}
            </div>
            {follow.error && (
              <p role="alert" className="text-destructive">
                <ErrorMessage error={follow.error} />
              </p>
            )}
            <ProfileStats runnerId={runnerId} />
            <ActivityCalendar runnerId={runnerId} />
            <GoalList key={`goals-${runnerId}`} runnerId={runnerId} />
            <h2 className="text-lg font-semibold">{t('nav.activities')}</h2>
            <ActivityFeed key={runnerId} runnerId={runnerId} />
          </>
        )}
      </SlowLoading>
    </section>
  )
}
