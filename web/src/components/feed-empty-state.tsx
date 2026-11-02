import { SlowLoading } from '@/components/slow-loading'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Plus, Users } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { ShareProfile } from '@/components/share-profile'
import { garminStatusQuery } from '@/components/garmin'
import { Button } from '@/components/ui/button'
import { followsQuery } from '@/lib/follows'
import { card } from '@/components/ui/card-variants'
import { Heading } from '@/components/ui/heading'

export function FeedEmptyState() {
  const { t } = useTranslation()
  const follows = useQuery(followsQuery)

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
            <OnboardingSteps />
          </section>
          <section aria-labelledby="social-start-heading" className={card()}>
            <Users aria-hidden="true" className="mb-4 size-5 text-muted-foreground" />
            <Heading level={3} id="social-start-heading">
              {t('feed.socialTitle')}
            </Heading>
            <p className="mb-4 mt-2 text-sm text-muted-foreground">{t('feed.socialDescription')}</p>
            <ShareProfile label={t('feed.copyProfileLink')} />
          </section>
        </div>
      ) : null}
    </SlowLoading>
  )
}

// Garmin first since it keeps activities coming, then Strava for the history Garmin does not fetch. The Garmin
// step only shows while the account can connect, so it appears for everyone once the connection opens.
function OnboardingSteps() {
  const { t } = useTranslation()
  const garmin = useQuery(garminStatusQuery)
  const steps: { key: 'garmin' | 'strava'; action: string }[] = [
    ...(garmin.data?.available && !garmin.data.connected
      ? [{ key: 'garmin' as const, action: t('garmin.connect') }]
      : []),
    { key: 'strava', action: t('feed.onboarding.stravaAction') },
  ]

  return (
    <>
      <ol aria-label={t('feed.onboarding.stepsLabel')} className="mt-6 space-y-5">
        {steps.map((step, index) => (
          <li key={step.key} className="flex gap-4">
            <span
              aria-hidden="true"
              className="flex size-8 shrink-0 items-center justify-center rounded-full bg-primary text-sm font-semibold text-primary-foreground"
            >
              {index + 1}
            </span>
            <div>
              <Heading level={3}>{t(`feed.onboarding.${step.key}Title`)}</Heading>
              <p className="mt-1 max-w-prose text-body text-muted-foreground">
                {t(`feed.onboarding.${step.key}Description`)}
              </p>
              <Button
                asChild
                variant={index === 0 ? 'primary-filled' : 'secondary-outline'}
                className="mt-3"
              >
                <Link to="/app/settings/import">{step.action}</Link>
              </Button>
            </div>
          </li>
        ))}
      </ol>
      <Button asChild variant="link" className="mt-6">
        <Link to="/app/activities/new">{t('feed.onboarding.manual')}</Link>
      </Button>
    </>
  )
}
