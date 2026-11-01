import { SlowLoading } from '@/components/slow-loading'
import { useQuery } from '@tanstack/react-query'
import { FileArchive, Plus, Users } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { ShareProfile } from '@/components/share-profile'
import { AddActivityButton, StravaImportLink } from '@/components/onboarding-actions'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import { card } from '@/components/ui/card-variants'
import { Heading } from '@/components/ui/heading'

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
          <Button variant="secondary-outline" onClick={() => void follows.refetch()}>
            {t('activities.retry')}
          </Button>
        </div>
      ) : follows.data ? (
        <div className="space-y-4">
          <section
            aria-labelledby="first-activity-heading"
            className={card({ tone: 'highlight', padding: 'spacious' })}
          >
            <div className="mb-5 flex size-12 items-center justify-center rounded-full bg-primary/10 text-primary">
              <Plus aria-hidden="true" className="size-6" />
            </div>
            <Heading level={2} id="first-activity-heading">
              {t(follows.data.following.length > 0 ? 'feed.waitingTitle' : 'feed.welcomeTitle')}
            </Heading>
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
            <section aria-labelledby="strava-start-heading" className={card()}>
              <FileArchive aria-hidden="true" className="mb-4 size-5 text-muted-foreground" />
              <Heading level={3} id="strava-start-heading">
                {t('feed.stravaTitle')}
              </Heading>
              <p className="mb-4 mt-2 text-sm text-muted-foreground">
                {t('feed.stravaDescription')}
              </p>
              <StravaImportLink />
            </section>
            <section aria-labelledby="social-start-heading" className={card()}>
              <Users aria-hidden="true" className="mb-4 size-5 text-muted-foreground" />
              <Heading level={3} id="social-start-heading">
                {t('feed.socialTitle')}
              </Heading>
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
