import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { meQuery } from '@/lib/auth'

// Garmin requires attribution on statistics and anything else derived from their data; own views only,
// since hasGarminData applies to the signed-in user.
export function GarminDerivedNote({ className }: { className?: string }) {
  const { data: me } = useQuery(meQuery)
  const { t } = useTranslation()
  if (!me?.hasGarminData) return null
  return <p className={`text-xs text-muted-foreground ${className ?? ''}`}>{t('garmin.derived')}</p>
}
