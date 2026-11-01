import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Settings } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { ActivitiesEmptyState } from '@/components/activities-empty-state'
import { ActivityFeed } from '@/components/activity-feed'
import { GoalList } from '@/components/goals'
import { ShareProfile } from '@/components/share-profile'
import { SupporterBadge } from '@/components/supporter'
import { meQuery } from '@/lib/auth'

export function ProfilePage() {
  const { t } = useTranslation()
  const { data: me } = useQuery(meQuery)

  return (
    <section className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <h1 className="flex min-w-0 items-center gap-2 break-words text-2xl font-semibold tracking-tight">
          {me?.displayName}
          <SupporterBadge tier={me?.supporter.tier} />
        </h1>
        <div className="flex max-w-full items-start gap-2">
          <ShareProfile />
          <Button asChild variant="ghost" size="icon">
            <Link to="/app/settings" aria-label={t('settings.title')}>
              <Settings aria-hidden="true" />
            </Link>
          </Button>
        </div>
      </div>
      {me && <GoalList runnerId={me.id} />}
      <h2 className="text-lg font-semibold">{t('nav.activities')}</h2>
      {me && (
        <ActivityFeed runnerId={me.id} emptyState={<ActivitiesEmptyState context="profile" />} />
      )}
    </section>
  )
}
