import { GarminDerivedNote } from '@/components/garmin'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { EquipmentSection } from '@/components/equipment'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
import { Heading } from '@/components/ui/heading'

export function EquipmentPage() {
  const { t } = useTranslation()
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
            <BreadcrumbLink render={<Link to="/app/settings" activeOptions={{ exact: true }} />}>
              {t('settings.title')}
            </BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>{t('equipment.title')}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <Heading level={1}>{t('equipment.title')}</Heading>
      <GarminDerivedNote className="mt-1" />
      <EquipmentSection />
    </section>
  )
}
