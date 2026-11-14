import { useState } from 'react'
import { useInfiniteQuery } from '@tanstack/react-query'
import { ChevronDown } from 'lucide-react'
import { Link, useRouterState } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { ActivityMapPreview } from '@/components/activity-map-preview'
import { QueryError } from '@/components/query-error'
import { RouteTrend, useSimilarRoutes } from '@/components/similar-routes'
import { SlowLoading } from '@/components/slow-loading'
import { Button } from '@/components/ui/button'
import { Stat } from '@/components/stat'
import { Heading } from '@/components/ui/heading'
import { card } from '@/components/ui/card-variants'
import { api } from '@/lib/api'
import { formatDuration } from '@/lib/activity'
import { sportGroupLabel } from '@/lib/sport-groups'

type RouteSummary = {
  id: string
  sportGroup: string
  count: number
  distanceM: number
  bestDurationS: number
  latestActivityId: string
  latestStartedAt: string
  routeGeoJson: string
  attribution: string | null
}

const pageSize = 10 // Routes.PageSize in the API

export function useRoutes() {
  return useInfiniteQuery({
    queryKey: ['activities', 'routes'],
    initialPageParam: 0,
    queryFn: ({ pageParam }) => api.get<RouteSummary[]>(`/api/routes?offset=${pageParam}`),
    // shortcut: a full last page costs one extra request that comes back empty
    getNextPageParam: (page, pages) =>
      page.length === pageSize ? pages.length * pageSize : undefined,
  })
}

// The three routes the runner has done most, of all time, with a link to the rest.
export function TopRoutesSection() {
  const { t } = useTranslation()
  const routes = useRoutes()
  const items = routes.data?.pages[0] ?? []
  if (routes.isError && !routes.data) return <QueryError query={routes} className="mt-10" />
  if (!items.length) return null

  return (
    <section className="mt-10" aria-labelledby="top-routes-title">
      <Heading level={2} id="top-routes-title">
        {t('topRoutes.title')}
      </Heading>
      <p className="mt-1 text-body text-muted-foreground">{t('topRoutes.description')}</p>
      <RouteList routes={items.slice(0, 3)} />
      {items.length > 3 && (
        <Button asChild variant="secondary-outline" className="mt-4">
          <Link to="/app/stats/routes">{t('topRoutes.all')}</Link>
        </Button>
      )}
    </section>
  )
}

// Each card links to the latest activity, whose page lists every run on the route.
export function RouteList({ routes }: { routes: RouteSummary[] }) {
  const { t, i18n } = useTranslation()
  const returnTo = useRouterState({ select: (s) => s.location.href })
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })
  const km = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 0 })
  if (!routes.length) return null
  return (
    <ol className="mt-4 grid gap-4 sm:grid-cols-2">
      {routes.map((route) => (
        <li key={route.id} className={card({ className: 'overflow-hidden' })}>
          <Heading level={3}>
            {t('topRoutes.times', {
              count: route.count,
              sport: sportGroupLabel(route.sportGroup, t),
            })}
          </Heading>
          {route.attribution && (
            <p className="text-metadata text-muted-foreground">{route.attribution}</p>
          )}
          <dl className="mt-3 grid grid-cols-2 gap-3">
            <Stat label={t('topRoutes.best')} value={formatDuration(route.bestDurationS)} />
            <Stat label={t('topRoutes.total')} value={`${km.format(route.distanceM / 1000)} km`} />
          </dl>
          <p className="mt-3 text-sm">
            {t('topRoutes.latest')}{' '}
            <Link
              to="/app/activities/$activityId"
              params={{ activityId: route.latestActivityId }}
              search={{ returnTo }}
              className="underline underline-offset-4"
            >
              {date.format(new Date(route.latestStartedAt))}
            </Link>
          </p>
          <RouteTrendToggle route={route} />
          <ActivityMapPreview geoJson={route.routeGeoJson} />
        </li>
      ))}
    </ol>
  )
}

// Time per run over time; the runs are fetched only when the chart is opened.
function RouteTrendToggle({ route }: { route: RouteSummary }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const similar = useSimilarRoutes(route.latestActivityId, open)
  const id = `route-trend-${route.id}`
  return (
    <div className="mt-3">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={id}
        onClick={() => setOpen(!open)}
        className="flex items-center gap-1 rounded-sm text-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      >
        {t('topRoutes.trend')}
        <ChevronDown
          aria-hidden="true"
          className={`size-4 transition-transform motion-reduce:transition-none ${open ? 'rotate-180' : ''}`}
        />
      </button>
      <div id={id} hidden={!open}>
        {similar.isError ? (
          <QueryError query={similar} className="mt-3" />
        ) : (
          <SlowLoading loading={similar.isPending && open}>
            {similar.data && similar.data.length > 1 && (
              <RouteTrend
                items={similar.data}
                activityId={route.latestActivityId}
                label={t('topRoutes.trendChart')}
              />
            )}
          </SlowLoading>
        )}
      </div>
    </div>
  )
}
