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
      <section className="space-y-6">
        <Heading level={1}>{t('nav.feed')}</Heading>
        <FollowRequests />
        <UnreviewedActivities />
        <ActivityFeed />
      </section>
    </div>
  )
}
