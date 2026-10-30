import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'

export function BackLink() {
  const { t } = useTranslation()
  return (
    <Link
      to="/app/activities"
      className="text-sm text-muted-foreground underline-offset-2 hover:underline"
    >
      ← {t('activity.back')}
    </Link>
  )
}
