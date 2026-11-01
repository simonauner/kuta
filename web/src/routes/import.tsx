import { useTranslation } from 'react-i18next'
import { GarminConnect, GarminConnectedAlert } from '@/components/garmin'
import { HelpPopover } from '@/components/help-popover'
import { SettingsBreadcrumb } from '@/components/settings-breadcrumb'
import { StravaImport } from '@/components/strava-import'
import { Heading } from '@/components/ui/heading'

// Everything that brings activities into Kuta from elsewhere: Garmin Connect and the Strava archive.
export function ImportPage() {
  const { t } = useTranslation()
  return (
    <section>
      <SettingsBreadcrumb page={t('settings.importTitle')} />
      <GarminConnectedAlert className="mb-4" />
      <Heading level={1}>{t('settings.importTitle')}</Heading>
      <p className="mt-2 text-body text-muted-foreground">{t('settings.importIntro')}</p>

      <section aria-labelledby="garmin-heading" className="mt-8 border-t pt-6">
        <Heading level={2} id="garmin-heading">
          {t('settings.garmin')}
        </Heading>
        <GarminConnect />
      </section>

      <section aria-labelledby="import-heading" className="mt-8 border-t pt-6">
        <div className="flex items-center justify-between gap-4">
          <Heading level={2} id="import-heading">
            {t('settings.strava')}
          </Heading>
          <HelpPopover label={t('settings.importDetailsLabel')}>
            {t('settings.importDetails')}
          </HelpPopover>
        </div>
        <StravaImport />
      </section>
    </section>
  )
}
