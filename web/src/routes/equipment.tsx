import { GarminDerivedNote } from '@/components/garmin'
import { useTranslation } from 'react-i18next'
import { EquipmentSection } from '@/components/equipment'
import { SettingsBreadcrumb } from '@/components/settings-breadcrumb'
import { Heading } from '@/components/ui/heading'

export function EquipmentPage() {
  const { t } = useTranslation()
  return (
    <section>
      <SettingsBreadcrumb page={t('equipment.title')} />
      <Heading level={1}>{t('equipment.title')}</Heading>
      <GarminDerivedNote className="mt-1" />
      <EquipmentSection />
    </section>
  )
}
