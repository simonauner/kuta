import { useMutation, useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { ActivityPrivacy } from '@/components/activity-privacy'
import { DangerZone } from '@/components/danger-zone'
import { ErrorMessage } from '@/components/error-message'
import { HeartRateConsent } from '@/components/heart-rate-consent'
import { LanguageSelect } from '@/components/language-select'
import { Recalculation } from '@/components/strava-import'
import { SupporterSection } from '@/components/supporter'
import { SettingsBreadcrumb } from '@/components/settings-breadcrumb'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import { meQuery, useSessionChanged } from '@/lib/auth'
import { card } from '@/components/ui/card-variants'
import { Heading } from '@/components/ui/heading'
import { HelpPopover } from '@/components/help-popover'

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
      <SettingsBreadcrumb />
      <Heading level={1}>{t('settings.title')}</Heading>
      <div className="mt-4 flex">
        <LanguageSelect />
      </div>
      <nav aria-label={t('settings.title')} className="mt-6 grid gap-3">
        {(
          [
            ['/app/settings/followers', t('settings.followers')],
            ['/app/settings/equipment', t('equipment.title')],
            ['/app/settings/privacy-places', t('privacyPlaces.title')],
            ['/app/settings/import', t('settings.importTitle')],
          ] as const
        ).map(([to, label]) => (
          <Link
            key={to}
            to={to}
            className={card({
              interactive: true,
              className: 'flex items-center justify-between font-medium',
            })}
          >
            {label}
            <span aria-hidden="true">→</span>
          </Link>
        ))}
      </nav>

      <ActivityPrivacy />

      <section aria-labelledby="health-heading" className="mt-8 border-t pt-6">
        <Heading level={2} id="health-heading">
          {t('settings.health')}
        </Heading>
        <HeartRateConsent className="mt-4" />
      </section>

      <SupporterSection />

      <section aria-labelledby="export-heading" className="mt-8 border-t pt-6">
        <div className="flex items-center justify-between gap-4">
          <Heading level={2} id="export-heading">
            {t('settings.exportTitle')}
          </Heading>
          <HelpPopover label={t('settings.exportDetailsLabel')}>
            {t('settings.exportDetails')}
          </HelpPopover>
        </div>
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

      <section aria-labelledby="recalculate-heading" className="mt-8 border-t pt-6">
        <Heading level={2} id="recalculate-heading">
          {t('settings.recalculateTitle')}
        </Heading>
        <Recalculation />
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
