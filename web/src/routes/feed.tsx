import { useTranslation } from 'react-i18next'
import { ActivityFeed } from '@/components/activity-feed'

export function FeedPage() {
  const { t } = useTranslation()
  return (
    <section className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">{t('nav.feed')}</h1>
      <ActivityFeed />
    </section>
  )
}
