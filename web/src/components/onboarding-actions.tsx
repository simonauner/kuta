import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { ArrowRight, Plus } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { garminStatusQuery } from '@/components/garmin'
import { Button } from '@/components/ui/button'

export function AddActivityButton({ className }: { className?: string }) {
  const { t } = useTranslation()
  return (
    <Button asChild size="lg" className={className}>
      <Link to="/app/activities/new">
        <Plus aria-hidden="true" />
        {t('onboarding.addActivity')}
      </Link>
    </Button>
  )
}

export function ImportLink({ className }: { className?: string }) {
  const { t } = useTranslation()
  // Mention Garmin only to accounts that can connect it, as in the feed onboarding.
  const { data: garmin } = useQuery(garminStatusQuery)
  return (
    <Button asChild variant="link" className={className}>
      <Link to="/app/settings/import">
        {t(garmin?.available && !garmin.connected ? 'settings.importLink' : 'settings.strava')}
        <ArrowRight aria-hidden="true" />
      </Link>
    </Button>
  )
}
