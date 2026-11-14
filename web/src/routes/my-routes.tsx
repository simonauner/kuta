import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { QueryError } from '@/components/query-error'
import { SlowLoading } from '@/components/slow-loading'
import { RouteList, useRoutes } from '@/components/top-routes'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
import { Button } from '@/components/ui/button'
import { Heading } from '@/components/ui/heading'

// All routes run at least twice, most runs first, ten at a time.
export function RoutesPage() {
  const { t } = useTranslation()
  const routes = useRoutes()
  const items = routes.data?.pages.flat() ?? []

  return (
    <section>
      <Breadcrumb aria-label={t('breadcrumbs')} className="mb-4">
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link to="/app/stats" activeOptions={{ exact: true }} />}>
              {t('nav.stats')}
            </BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>{t('topRoutes.title')}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <Heading level={1}>{t('topRoutes.title')}</Heading>
      <p className="mt-1 text-body text-muted-foreground">{t('topRoutes.description')}</p>
      {routes.isError && !routes.data ? (
        <QueryError query={routes} className="mt-4" />
      ) : (
        <SlowLoading loading={routes.isPending}>
          {routes.isSuccess && items.length === 0 && (
            <p className="mt-4 text-body text-muted-foreground">{t('topRoutes.empty')}</p>
          )}
          <RouteList routes={items} />
        </SlowLoading>
      )}
      <SlowLoading loading={routes.isFetchingNextPage}>
        {routes.hasNextPage && (
          <Button
            variant="secondary-outline"
            className="mt-4"
            disabled={routes.isFetching}
            onClick={() => void routes.fetchNextPage()}
          >
            {routes.isError ? t('topRoutes.retry') : t('topRoutes.more')}
          </Button>
        )}
      </SlowLoading>
    </section>
  )
}
