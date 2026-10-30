import { useMutation, useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { DeleteAllActivities } from '@/components/delete-all-activities'
import { ErrorMessage } from '@/components/error-message'
import { HeartRateConsent } from '@/components/heart-rate-consent'
import { LanguageSelect } from '@/components/language-select'
import { StravaImport } from '@/components/strava-import'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import { meQuery, useSessionChanged } from '@/lib/auth'

export function SettingsPage() {
  const { t } = useTranslation()
  const { data: me } = useQuery(meQuery)
  const sessionChanged = useSessionChanged()
  const logout = useMutation({
    mutationFn: () => api.send('POST', '/api/auth/logout'),
    onSuccess: sessionChanged,
  })

  return (
    <section>
      <Link
        to="/app/profile"
        className="text-sm text-muted-foreground underline-offset-2 hover:underline"
      >
        {t('settings.back')}
      </Link>
      <h1 className="mt-4 text-2xl font-semibold tracking-tight">{t('settings.title')}</h1>
      <div className="mt-6 flex">
        <LanguageSelect />
      </div>

      <section aria-labelledby="health-heading" className="mt-8 border-t pt-6">
        <h2 id="health-heading" className="text-lg font-semibold">
          {t('settings.health')}
        </h2>
        <HeartRateConsent className="mt-4" />
      </section>

      <section aria-labelledby="import-heading" className="mt-8 border-t pt-6">
        <h2 id="import-heading" className="text-lg font-semibold">
          {t('settings.strava')}
        </h2>
        <StravaImport />
      </section>

      <section aria-labelledby="account-heading" className="mt-8 border-t pt-6">
        <h2 id="account-heading" className="text-lg font-semibold">
          {t('settings.account')}
        </h2>
        <p className="mt-2 break-words text-sm text-muted-foreground">{me?.email}</p>
        <Button
          variant="outline"
          className="mt-4"
          disabled={logout.isPending}
          onClick={() => logout.mutate()}
        >
          {t('settings.logout')}
        </Button>
        {logout.error && (
          <p role="alert" className="mt-4 text-sm text-destructive">
            <ErrorMessage error={logout.error} />
          </p>
        )}
      </section>
      <DeleteAllActivities />
    </section>
  )
}
