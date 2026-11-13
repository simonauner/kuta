import { useQuery } from '@tanstack/react-query'
import { Link, useRouterState } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { ActivityMapPreview } from '@/components/activity-map-preview'
import { QueryError } from '@/components/query-error'
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

// The routes the runner has done most, of all time. The latest activity's page lists every run on the route.
export function TopRoutesSection() {
  const { t, i18n } = useTranslation()
  const returnTo = useRouterState({ select: (s) => s.location.href })
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })
  const km = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 0 })
  const routes = useQuery({
    queryKey: ['activities', 'routes'],
    queryFn: () => api.get<RouteSummary[]>('/api/routes'),
  })
  if (routes.isError) return <QueryError query={routes} className="mt-10" />
  if (!routes.data?.length) return null

  return (
    <section className="mt-10" aria-labelledby="top-routes-title">
      <Heading level={2} id="top-routes-title">
        {t('topRoutes.title')}
      </Heading>
      <p className="mt-1 text-body text-muted-foreground">{t('topRoutes.description')}</p>
      <ol className="mt-4 grid gap-4 sm:grid-cols-2">
        {routes.data.map((route) => (
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
              <Stat
                label={t('topRoutes.total')}
                value={`${km.format(route.distanceM / 1000)} km`}
              />
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
            <ActivityMapPreview geoJson={route.routeGeoJson} />
          </li>
        ))}
      </ol>
    </section>
  )
}
