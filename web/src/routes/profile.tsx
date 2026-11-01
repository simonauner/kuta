import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Eye, Settings } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import { ActivitiesEmptyState } from '@/components/activities-empty-state'
import { ActivityFeed } from '@/components/activity-feed'
import { GarminDerivedNote } from '@/components/garmin'
import { GoalList } from '@/components/goals'
import {
  ActivityCalendar,
  ProfileStats,
  RecordShelf,
  type ViewAs,
} from '@/components/runner-summary'
import { ShareProfile } from '@/components/share-profile'
import { SupporterBadge } from '@/components/supporter'
import { api } from '@/lib/api'
import { meQuery, type SupporterTier } from '@/lib/auth'

export function ProfilePage() {
  const { t } = useTranslation()
  const { data: me } = useQuery(meQuery)
  const [viewAs, setViewAs] = useState<ViewAs | undefined>()
  // The badge follows its own visibility setting, so the preview asks the API like a visitor would.
  const preview = useQuery({
    queryKey: ['runner', me?.id, viewAs],
    queryFn: () =>
      api.get<{ supporterTier: SupporterTier | null }>(`/api/runners/${me!.id}?viewAs=${viewAs}`),
    enabled: !!me && !!viewAs,
  })
  const tier = viewAs ? preview.data?.supporterTier : me?.supporter.tier

  return (
    <section className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <h1 className="flex min-w-0 items-center gap-2 break-words text-2xl font-semibold tracking-tight">
          {me?.displayName}
          <SupporterBadge tier={tier} />
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
      <FormField layout="inline" label={t('profile.viewAs.label')} className="text-sm">
        {(id) => (
          <select
            id={id}
            className="rounded-md border bg-background px-3 py-2 text-foreground"
            value={viewAs ?? ''}
            onChange={(e) => setViewAs((e.target.value || undefined) as ViewAs | undefined)}
          >
            <option value="">{t('profile.viewAs.me')}</option>
            <option value="followers">{t('profile.viewAs.followers')}</option>
            <option value="public">{t('profile.viewAs.public')}</option>
          </select>
        )}
      </FormField>
      {viewAs && (
        <Alert>
          <Eye aria-hidden="true" />
          <AlertDescription>{t(`profile.viewAs.${viewAs}Hint`)}</AlertDescription>
        </Alert>
      )}
      {me && (
        // Keyed by viewAs so each view starts fresh instead of showing the previous one while loading.
        <div key={viewAs ?? 'me'} className="space-y-6">
          <ProfileStats runnerId={me.id} viewAs={viewAs} />
          <ActivityCalendar runnerId={me.id} viewAs={viewAs} />
          <RecordShelf runnerId={me.id} viewAs={viewAs} />
          {!viewAs && <GarminDerivedNote />}
          <GoalList runnerId={me.id} viewAs={viewAs} />
          <h2 className="text-lg font-semibold">{t('nav.activities')}</h2>
          <ActivityFeed
            runnerId={me.id}
            viewAs={viewAs}
            emptyState={viewAs ? undefined : <ActivitiesEmptyState context="profile" />}
          />
        </div>
      )}
    </section>
  )
}
