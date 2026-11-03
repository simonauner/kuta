import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ActivityFeed } from '@/components/activity-feed'
import { FeedSummary } from '@/components/feed-summary'
import { runnerSummaryQuery } from '@/components/runner-summary'
import { UnreviewedActivities } from '@/components/unreviewed-activities'
import { FollowRequests } from '@/components/follow-requests'
import { Heading } from '@/components/ui/heading'
import { meQuery } from '@/lib/auth'

export function FeedPage() {
  const { t } = useTranslation()
  const { data: me } = useQuery(meQuery)
  const { data: summary } = useQuery({ ...runnerSummaryQuery(me?.id ?? ''), enabled: !!me })
  // Keep the column while loading so the feed does not jump; drop it once there is nothing to sum up.
  const showSummary = summary?.allTime.count !== 0
  return (
    <div
      className={
        showSummary
          ? 'lg:grid lg:grid-cols-[18rem_minmax(0,1fr)] lg:items-start lg:gap-8'
          : undefined
      }
    >
      {/* Small screens reach the same numbers on Profile, so the feed comes first there. */}
      {showSummary && (
        <aside className="hidden lg:block">
          <FeedSummary />
        </aside>
      )}
      <section className="flex flex-col gap-6">
        {/* The header with the logo is a bottom bar on small screens, so the start page shows the brand there
            instead of the heading, which the active Feed tab already names; screen readers keep the heading. */}
        <picture className="block md:hidden">
          <source media="(prefers-color-scheme: dark)" srcSet="/brand/logo-light.svg" />
          <img src="/brand/logo.svg" alt="Kuta" width="96" height="32" className="h-8 w-auto" />
        </picture>
        <Heading level={1} className="sr-only md:not-sr-only">
          {t('nav.feed')}
        </Heading>
        <FollowRequests />
        <UnreviewedActivities />
        <ActivityFeed />
      </section>
    </div>
  )
}
