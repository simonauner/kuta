import { Link } from '@tanstack/react-router'
import { ArrowRight, Plus } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

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

export function StravaImportLink({ className }: { className?: string }) {
  const { t } = useTranslation()
  return (
    <Button asChild variant="link" className={cn('h-auto whitespace-normal', className)}>
      <Link to="/app/settings" hash="import-heading">
        {t('settings.strava')}
        <ArrowRight aria-hidden="true" />
      </Link>
    </Button>
  )
}
