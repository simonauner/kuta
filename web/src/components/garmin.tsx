import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { meQuery } from '@/lib/auth'

// Garmin kräver märkning av statistik och annat som härleds ur deras data; bara för egna vyer,
// eftersom hasGarminData gäller den inloggade.
export function GarminDerivedNote({ className }: { className?: string }) {
  const { data: me } = useQuery(meQuery)
  const { t } = useTranslation()
  if (!me?.hasGarminData) return null
  return <p className={`text-xs text-muted-foreground ${className ?? ''}`}>{t('garmin.derived')}</p>
}
