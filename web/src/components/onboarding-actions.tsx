import { Link } from '@tanstack/react-router'
import { ArrowRight, Plus } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'

export function AddActivityButton({ className }: { className?: string }) {
  const { t } = useTranslation()
  return (
    <Button size="lg" className={className} render={<Link to="/app/activities/new" />}>
      <Plus aria-hidden="true" />
      {t('onboarding.addActivity')}
    </Button>
  )
}

export function ImportLink({ className }: { className?: string }) {
  const { t } = useTranslation()
  return (
    <Button variant="link" className={className} render={<Link to="/app/settings/import" />}>
      {t('settings.importLink')}
      <ArrowRight aria-hidden="true" />
    </Button>
  )
}
