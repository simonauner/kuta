import { SlowLoading } from '@/components/slow-loading'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { ExternalLink, Plus, Users } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { QueryError } from '@/components/query-error'
import { AddActivityButton } from '@/components/onboarding-actions'
import { ShareProfile } from '@/components/share-profile'
import { garminStatusQuery } from '@/components/garmin'
import { Button } from '@/components/ui/button'
import { followsQuery } from '@/lib/follows'
import { card } from '@/components/ui/card-variants'
import { Heading } from '@/components/ui/heading'
import { stravaExportUrl } from '@/lib/strava'

export function FeedEmptyState() {
  const { t } = useTranslation()
  const follows = useQuery(followsQuery)

  return (
    <SlowLoading loading={follows.isPending}>
      {follows.isError ? (
        <QueryError query={follows} />
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
            <ShareProfile label={t('feed.shareProfileLink')} />
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
      {/* Adding an activity works right away, so it leads; connections bring in more but take a detour. */}
      <AddActivityButton className="mt-6" />
      <p id="onboarding-steps-label" className="mt-8 text-metadata text-muted-foreground">
        {t('feed.onboarding.stepsLabel')}
      </p>
      <ul aria-labelledby="onboarding-steps-label" className="mt-2 divide-y border-t">
        {steps.map((step) => (
          <li key={step.key} className="py-4">
            <Heading level={3}>{t(`feed.onboarding.${step.key}Title`)}</Heading>
            <p className="mt-1 max-w-prose text-body text-muted-foreground">
              {t(`feed.onboarding.${step.key}Description`)}
            </p>
            <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-2">
              <Button asChild variant="secondary-outline">
                <Link to="/app/settings/import">{step.action}</Link>
              </Button>
              {/* The archive takes hours to arrive, so it can be requested straight away. */}
              {step.key === 'strava' && (
                <Button asChild variant="link">
                  <a href={stravaExportUrl} target="_blank" rel="noopener noreferrer">
                    {t('feed.onboarding.stravaRequest')}
                    <ExternalLink aria-hidden="true" />
                  </a>
                </Button>
              )}
            </div>
          </li>
        ))}
      </ul>
    </>
  )
}
