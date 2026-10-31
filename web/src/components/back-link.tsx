import { Link, useSearch } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'

export function BackLink() {
  const { t } = useTranslation()
  const { year, tag } = useSearch({ strict: false })
  return (
    <Link
      to="/app/activities"
      search={{ year, tag }}
      className="text-sm text-muted-foreground underline-offset-2 hover:underline"
    >
      ← {t('activity.back')}
    </Link>
  )
}
