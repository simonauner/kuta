import { SlowLoading } from '@/components/slow-loading'
import { useQuery } from '@tanstack/react-query'
import { FileArchive, Plus, Users } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { ShareProfile } from '@/components/share-profile'
import { AddActivityButton, StravaImportLink } from '@/components/onboarding-actions'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'

export function FeedEmptyState() {
  const { t } = useTranslation()
  const follows = useQuery({
    queryKey: ['follows'],
    queryFn: () => api.get<{ following: { id: string }[] }>('/api/follows'),
  })

  return (
    <SlowLoading loading={follows.isPending}>
      {follows.isError ? (
        <div role="alert" className="space-y-3">
          <ErrorMessage error={follows.error} />
          <Button variant="outline" onClick={() => void follows.refetch()}>
            {t('activities.retry')}
          </Button>
        </div>
      ) : follows.data ? (
        <div className="space-y-4">
          <section
            aria-labelledby="first-activity-heading"
            className="rounded-xl border bg-primary/5 p-6 sm:p-8"
          >
            <div className="mb-5 flex size-12 items-center justify-center rounded-full bg-primary/10 text-primary">
              <Plus aria-hidden="true" className="size-6" />
            </div>
            <h2 id="first-activity-heading" className="text-xl font-semibold tracking-tight">
              {t(follows.data.following.length > 0 ? 'feed.waitingTitle' : 'feed.welcomeTitle')}
            </h2>
            <p className="mt-3 max-w-prose text-muted-foreground">
              {t(
                follows.data.following.length > 0
                  ? 'feed.waitingDescription'
                  : 'feed.welcomeDescription',
              )}
            </p>
            <AddActivityButton className="mt-6" />
            <p className="mt-3 text-sm text-muted-foreground">{t('feed.addActivityHelp')}</p>
          </section>
          <div className="grid gap-4 sm:grid-cols-2">
            <section aria-labelledby="strava-start-heading" className="rounded-xl border p-5">
              <FileArchive aria-hidden="true" className="mb-4 size-5 text-muted-foreground" />
              <h2 id="strava-start-heading" className="font-semibold">
                {t('feed.stravaTitle')}
              </h2>
              <p className="mb-4 mt-2 text-sm text-muted-foreground">
                {t('feed.stravaDescription')}
              </p>
              <StravaImportLink className="p-0 text-left" />
            </section>
            <section aria-labelledby="social-start-heading" className="rounded-xl border p-5">
              <Users aria-hidden="true" className="mb-4 size-5 text-muted-foreground" />
              <h2 id="social-start-heading" className="font-semibold">
                {t('feed.socialTitle')}
              </h2>
              <p className="mb-4 mt-2 text-sm text-muted-foreground">
                {t('feed.socialDescription')}
              </p>
              <ShareProfile label={t('feed.copyProfileLink')} />
            </section>
          </div>
        </div>
      ) : null}
    </SlowLoading>
  )
}
