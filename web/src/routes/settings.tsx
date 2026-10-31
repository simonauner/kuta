import { useMutation, useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { ActivityPrivacy } from '@/components/activity-privacy'
import { GoalPrivacy } from '@/components/goal-privacy'
import { DangerZone } from '@/components/danger-zone'
import { ErrorMessage } from '@/components/error-message'
import { HeartRateConsent } from '@/components/heart-rate-consent'
import { LanguageSelect } from '@/components/language-select'
import { StravaImport } from '@/components/strava-import'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
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

  const dataExport = useMutation({
    mutationFn: () => api.download('/api/me/export', 'kuta-export.zip'),
  })

  return (
    <section>
      <Breadcrumb aria-label={t('breadcrumbs')} className="mb-4">
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link to="/app/profile" activeOptions={{ exact: true }} />}>
              {t('nav.profile')}
            </BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>{t('settings.title')}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <h1 className="text-2xl font-semibold tracking-tight">{t('settings.title')}</h1>
      <div className="mt-6 grid gap-3">
        <Link
          to="/app/settings/followers"
          className="flex items-center justify-between rounded-lg border p-4 font-medium hover:bg-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          {t('settings.followers')}
          <span aria-hidden="true">→</span>
        </Link>
        <Link
          to="/app/settings/equipment"
          className="flex items-center justify-between rounded-lg border p-4 font-medium hover:bg-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          {t('equipment.title')}
          <span aria-hidden="true">→</span>
        </Link>
      </div>
      <div className="mt-6 flex">
        <LanguageSelect />
      </div>

      <Link
        to="/app/settings/privacy-places"
        className="mt-8 flex items-center justify-between rounded-lg border p-4 font-medium hover:bg-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      >
        {t('privacyPlaces.title')}
        <span aria-hidden="true">→</span>
      </Link>

      <ActivityPrivacy />
      <GoalPrivacy />

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

      <section aria-labelledby="export-heading" className="mt-8 border-t pt-6">
        <h2 id="export-heading" className="text-lg font-semibold">
          {t('settings.exportTitle')}
        </h2>
        <p className="mt-2 text-sm text-muted-foreground">{t('settings.exportHelp')}</p>
        <Button
          variant="outline"
          className="mt-4"
          disabled={dataExport.isPending}
          onClick={() => dataExport.mutate()}
        >
          {t(dataExport.isPending ? 'settings.exporting' : 'settings.export')}
        </Button>
        {dataExport.isPending && (
          <p role="status" className="mt-2 text-sm text-muted-foreground">
            {t('settings.exportWait')}
          </p>
        )}
        {dataExport.error && (
          <p role="alert" className="mt-4 text-sm text-destructive">
            <ErrorMessage error={dataExport.error} />
          </p>
        )}
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
      <DangerZone />
    </section>
  )
}
