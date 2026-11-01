import { useTranslation } from 'react-i18next'
import { PrivacyPlaces } from '@/components/privacy-places'
import { SettingsBreadcrumb } from '@/components/settings-breadcrumb'

export function PrivacyPlacesPage() {
  const { t } = useTranslation()
  return (
    <section>
      <SettingsBreadcrumb page={t('privacyPlaces.title')} />
      <PrivacyPlaces />
    </section>
  )
}
