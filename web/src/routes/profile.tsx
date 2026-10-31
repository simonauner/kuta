import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Settings } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { meQuery } from '@/lib/auth'

export function ProfilePage() {
  const { t } = useTranslation()
  const { data: me } = useQuery(meQuery)

  return (
    <section>
      <div className="flex items-start justify-between gap-4">
        <h1 className="min-w-0 break-words text-2xl font-semibold tracking-tight">
          {me?.displayName}
        </h1>
        <Button asChild variant="ghost" size="icon">
          <Link to="/app/settings" aria-label={t('settings.title')}>
            <Settings aria-hidden="true" />
          </Link>
        </Button>
      </div>
      <p className="mt-6 text-muted-foreground">{t('profile.description')}</p>
      <Link
        to="/app/profile/equipment"
        className="mt-6 flex items-center justify-between rounded-lg border p-4 font-medium hover:bg-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      >
        {t('equipment.title')}
        <span aria-hidden="true">→</span>
      </Link>
    </section>
  )
}
