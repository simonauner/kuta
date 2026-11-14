import { useQuery } from '@tanstack/react-query'
import { getRouteApi, Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { FormField } from '@/components/ui/form-field'
import { QueryError } from '@/components/query-error'
import { SlowLoading } from '@/components/slow-loading'
import { GroupingNote, RouteList, useRoutes, type RouteFilters } from '@/components/top-routes'
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
import { Select } from '@/components/ui/select'
import { activityYearsQuery, sportGroupsQuery } from '@/lib/activity-query'
import { sportGroupLabel } from '@/lib/sport-groups'

export function routeSearch(search: Record<string, unknown>): RouteFilters {
  const year = Number(search.year)
  const filters: RouteFilters = {}
  if (Number.isInteger(year) && year >= 1 && year <= 9998) filters.year = year
  // The API rejects unknown groups, which then shows as an error
  if (typeof search.sportGroup === 'string' && search.sportGroup)
    filters.sportGroup = search.sportGroup
  return filters
}

const filtersKey = 'kuta.routeFilters'

// The last chosen filters in this browser, used when the page opens without filters in the URL.
export function savedRouteFilters(): RouteFilters {
  try {
    return routeSearch(
      JSON.parse(localStorage.getItem(filtersKey) ?? '{}') as Record<string, unknown>,
    )
  } catch {
    return {}
  }
}

const route = getRouteApi('/app/stats/routes')

// All routes run at least twice, most runs first, ten at a time.
export function RoutesPage() {
  const { t } = useTranslation()
  const { year, sportGroup } = route.useSearch()
  const navigate = route.useNavigate()
  const years = useQuery(activityYearsQuery(Intl.DateTimeFormat().resolvedOptions().timeZone))
  const sportGroups = useQuery(sportGroupsQuery)
  const routes = useRoutes({ year, sportGroup })
  // Saved before navigating, so choosing all years and sports is not replaced by the previous choice.
  const setFilters = (filters: RouteFilters) => {
    try {
      localStorage.setItem(filtersKey, JSON.stringify(filters))
    } catch {
      // Blocked storage only forgets the choice.
    }
    void navigate({ search: filters })
  }
  const items = routes.data?.pages.flatMap((page) => page.items) ?? []
  const grouping = routes.data?.pages[0]?.grouping

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
      <p className="mt-1 text-body text-muted-foreground">
        {year === undefined ? t('topRoutes.description') : t('topRoutes.descriptionYear', { year })}
      </p>
      <div className="mt-4 flex flex-wrap gap-x-6 gap-y-3">
        <FormField layout="inline" label={t('activities.year')} className="text-sm">
          {(id) => (
            <Select
              id={id}
              className="w-32"
              value={year ?? ''}
              onChange={(e) =>
                setFilters({
                  year: e.target.value ? Number(e.target.value) : undefined,
                  sportGroup,
                })
              }
            >
              <option value="">{t('activities.allYears')}</option>
              {year !== undefined && !years.data?.includes(year) && (
                <option value={year}>{year}</option>
              )}
              {years.data?.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </Select>
          )}
        </FormField>
        <FormField layout="inline" label={t('activities.sport')} className="text-sm">
          {(id) => (
            <Select
              id={id}
              className="w-fit"
              value={sportGroup ?? ''}
              onChange={(e) => setFilters({ year, sportGroup: e.target.value || undefined })}
            >
              <option value="">{t('activities.allSports')}</option>
              {(sportGroups.data ?? (sportGroup ? [{ key: sportGroup }] : [])).map(({ key }) => (
                <option key={key} value={key}>
                  {sportGroupLabel(key, t)}
                </option>
              ))}
            </Select>
          )}
        </FormField>
      </div>
      {routes.isError && !routes.data ? (
        <QueryError query={routes} className="mt-4" />
      ) : (
        <SlowLoading loading={routes.isPending}>
          {grouping && <GroupingNote className="mt-4" />}
          {routes.isSuccess && !grouping && items.length === 0 && (
            <p className="mt-4 text-body text-muted-foreground">
              {t(year !== undefined || sportGroup ? 'topRoutes.noMatches' : 'topRoutes.empty')}
            </p>
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
