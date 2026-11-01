import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'

// Profile → Settings → the current page; without `page`, Settings itself is the current page.
export function SettingsBreadcrumb({ page }: { page?: string }) {
  const { t } = useTranslation()
  return (
    <Breadcrumb aria-label={t('breadcrumbs')} className="mb-4">
      <BreadcrumbList>
        <BreadcrumbItem>
          <BreadcrumbLink render={<Link to="/app/profile" activeOptions={{ exact: true }} />}>
            {t('nav.profile')}
          </BreadcrumbLink>
        </BreadcrumbItem>
        <BreadcrumbSeparator />
        <BreadcrumbItem>
          {page ? (
            <BreadcrumbLink render={<Link to="/app/settings" activeOptions={{ exact: true }} />}>
              {t('settings.title')}
            </BreadcrumbLink>
          ) : (
            <BreadcrumbPage>{t('settings.title')}</BreadcrumbPage>
          )}
        </BreadcrumbItem>
        {page && (
          <>
            <BreadcrumbSeparator />
            <BreadcrumbItem>
              <BreadcrumbPage>{page}</BreadcrumbPage>
            </BreadcrumbItem>
          </>
        )}
      </BreadcrumbList>
    </Breadcrumb>
  )
}
