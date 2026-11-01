import { useMutation, useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { ActivityPrivacy } from '@/components/activity-privacy'
import { DangerZone } from '@/components/danger-zone'
import { ErrorMessage } from '@/components/error-message'
import { GarminConnect, GarminConnectedAlert } from '@/components/garmin'
import { HeartRateConsent } from '@/components/heart-rate-consent'
import { LanguageSelect } from '@/components/language-select'
import { Recalculation, StravaImport } from '@/components/strava-import'
import { SupporterSection } from '@/components/supporter'
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
import { card } from '@/components/ui/card-variants'
import { Heading } from '@/components/ui/heading'

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
      <GarminConnectedAlert className="mb-4" />
      <Heading level={1}>{t('settings.title')}</Heading>
      <div className="mt-4 flex">
        <LanguageSelect />
      </div>
      <div className="mt-6 grid gap-3">
        <Link
          to="/app/settings/followers"
          className={card({
            interactive: true,
            className: 'flex items-center justify-between font-medium',
          })}
        >
          {t('settings.followers')}
          <span aria-hidden="true">→</span>
        </Link>
        <Link
          to="/app/settings/equipment"
          className={card({
            interactive: true,
            className: 'flex items-center justify-between font-medium',
          })}
        >
          {t('equipment.title')}
          <span aria-hidden="true">→</span>
        </Link>
        <Link
          to="/app/settings/privacy-places"
          className={card({
            interactive: true,
            className: 'flex items-center justify-between font-medium',
          })}
        >
          {t('privacyPlaces.title')}
          <span aria-hidden="true">→</span>
        </Link>
      </div>

      <SupporterSection />

      <ActivityPrivacy />

      <section aria-labelledby="health-heading" className="mt-8 border-t pt-6">
        <Heading level={2} id="health-heading">
          {t('settings.health')}
        </Heading>
        <HeartRateConsent className="mt-4" />
      </section>

      <section aria-labelledby="garmin-heading" className="mt-8 border-t pt-6">
        <Heading level={2} id="garmin-heading" className="scroll-mt-24">
          {t('settings.garmin')}
        </Heading>
        <GarminConnect />
      </section>

      <section aria-labelledby="import-heading" className="mt-8 border-t pt-6">
        <Heading level={2} id="import-heading" className="scroll-mt-24">
          {t('settings.strava')}
        </Heading>
        <StravaImport />
      </section>

      <section aria-labelledby="recalculate-heading" className="mt-8 border-t pt-6">
        <Heading level={2} id="recalculate-heading">
          {t('settings.recalculateTitle')}
        </Heading>
        <Recalculation />
      </section>

      <section aria-labelledby="export-heading" className="mt-8 border-t pt-6">
        <Heading level={2} id="export-heading">
          {t('settings.exportTitle')}
        </Heading>
        <p className="mt-2 text-sm text-muted-foreground">{t('settings.exportHelp')}</p>
        <Button
          variant="secondary-outline"
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
        <Heading level={2} id="account-heading">
          {t('settings.account')}
        </Heading>
        <p className="mt-2 break-words text-sm text-muted-foreground">{me?.email}</p>
        <Button
          variant="secondary-outline"
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
