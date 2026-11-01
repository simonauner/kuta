import { useTranslation } from 'react-i18next'
import { ActivityFeed } from '@/components/activity-feed'
import { UnreviewedActivities } from '@/components/unreviewed-activities'
import { FollowRequests } from '@/components/follow-requests'
import { Heading } from '@/components/ui/heading'

export function FeedPage() {
  const { t } = useTranslation()
  return (
    <section className="space-y-6">
      <Heading level={1}>{t('nav.feed')}</Heading>
      <FollowRequests />
      <UnreviewedActivities />
      <ActivityFeed />
    </section>
  )
}
