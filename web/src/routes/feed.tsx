import { useTranslation } from 'react-i18next'
import { ActivityFeed } from '@/components/activity-feed'
import { FeedSummary } from '@/components/feed-summary'
import { UnreviewedActivities } from '@/components/unreviewed-activities'
import { FollowRequests } from '@/components/follow-requests'
import { Heading } from '@/components/ui/heading'

export function FeedPage() {
  const { t } = useTranslation()
  return (
    <div className="lg:grid lg:grid-cols-[18rem_minmax(0,1fr)] lg:items-start lg:gap-8">
      {/* Small screens reach the same numbers on Profile, so the feed comes first there. */}
      <aside className="hidden lg:block">
        <FeedSummary />
      </aside>
      <section className="space-y-6">
        <Heading level={1}>{t('nav.feed')}</Heading>
        <FollowRequests />
        <UnreviewedActivities />
        <ActivityFeed />
      </section>
    </div>
  )
}
