import { useQuery } from '@tanstack/react-query'
import { Link, useRouterState } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { Heading } from '@/components/ui/heading'
import { QueryError } from '@/components/query-error'
import { api } from '@/lib/api'
import { formatDuration, formatSportPace } from '@/lib/activity'
import { durationTicks } from '@/lib/duration-ticks'

type SimilarRouteActivity = {
  id: string
  sport: string
  title: string | null
  startedAt: string
  durationS: number
  movingS: number | null
  distanceM: number
  attribution: string | null
}

// The owner's activities on the same route, newest first; shown when there is at least one other.
export function SimilarRoutes({ activityId }: { activityId: string }) {
  const { t, i18n } = useTranslation()
  const returnTo = useRouterState({ select: (s) => s.location.href })
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })
  const similar = useQuery({
    queryKey: ['activities', activityId, 'similar'],
    queryFn: () => api.get<SimilarRouteActivity[]>(`/api/activities/${activityId}/similar`),
  })
  if (similar.isError) return <QueryError query={similar} className="mt-4" />
  const items = similar.data ?? []
  if (items.length < 2) return null
  const fastest = items.reduce((best, a) => (a.durationS < best.durationS ? a : best))
  const rank = items.filter(
    (a) => a.durationS < items.find((x) => x.id === activityId)!.durationS,
  ).length

  return (
    <section className="mt-8" aria-labelledby="similar-routes-title">
      <Heading level={2} id="similar-routes-title" className="mb-2">
        {t('similarRoutes.title')}
      </Heading>
      <p className="text-body text-muted-foreground">
        {t('similarRoutes.summary', { count: items.length, rank: rank + 1 })}
      </p>
      {[...new Set(items.map((a) => a.attribution).filter(Boolean))].map((attribution) => (
        <p key={attribution} className="text-metadata text-muted-foreground">
          {attribution}
        </p>
      ))}
      {items.length > 2 && <RouteTrend items={items} activityId={activityId} />}
      <table className="mt-3 w-full text-sm tabular-nums">
        <thead className="text-left text-xs text-muted-foreground">
          <tr>
            <th className="py-1 font-normal">{t('similarRoutes.date')}</th>
            <th className="py-1 font-normal">{t('activity.time')}</th>
            <th className="py-1 font-normal">{t('activity.pace')}</th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {items.map((a) => (
            <tr key={a.id} aria-current={a.id === activityId ? 'page' : undefined}>
              <td className="py-1.5">
                {a.id === activityId ? (
                  <span className="font-medium">
                    {date.format(new Date(a.startedAt))} ({t('similarRoutes.current')})
                  </span>
                ) : (
                  <Link
                    to="/app/activities/$activityId"
                    params={{ activityId: a.id }}
                    search={{ returnTo }}
                    className="underline underline-offset-4"
                  >
                    {date.format(new Date(a.startedAt))}
                  </Link>
                )}
              </td>
              <td className="py-1.5">
                {formatDuration(a.durationS)}
                {a.id === fastest.id && (
                  <span className="ml-2 text-metadata text-muted-foreground">
                    {t('similarRoutes.fastest')}
                  </span>
                )}
              </td>
              <td className="py-1.5">
                {formatSportPace(a.sport, a.durationS, a.distanceM, i18n.resolvedLanguage)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}

// Time per activity over time, faster higher, with gridlines at round times; the current activity is filled.
function RouteTrend({ items, activityId }: { items: SimilarRouteActivity[]; activityId: string }) {
  const { t, i18n } = useTranslation()
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })
  const [width, height, pad, left] = [600, 140, 8, 56]
  const times = items.map((a) => new Date(a.startedAt).getTime())
  const [first, last] = [Math.min(...times), Math.max(...times)]
  const durations = items.map((a) => a.durationS)
  const { lo, hi, ticks } = durationTicks(Math.min(...durations), Math.max(...durations))
  const y = (seconds: number) => pad + ((seconds - lo) / (hi - lo)) * (height - 2 * pad)
  const points = items
    .map((a, i) => ({
      a,
      x: left + pad + ((times[i] - first) / (last - first || 1)) * (width - left - 2 * pad),
      y: y(a.durationS),
    }))
    .reverse()
  return (
    <figure className="mt-3">
      <svg
        viewBox={`0 0 ${width} ${height}`}
        className="w-full overflow-visible text-primary"
        role="img"
        aria-label={t('similarRoutes.trendChart')}
      >
        {ticks.map((tick) => (
          <g key={tick}>
            <line x1={left} x2={width} y1={y(tick)} y2={y(tick)} stroke="var(--border)" />
            <text
              x={left - 8}
              y={y(tick)}
              textAnchor="end"
              dominantBaseline="middle"
              fontSize="12"
              fill="var(--muted-foreground)"
              className="tabular-nums"
            >
              {formatDuration(tick)}
            </text>
          </g>
        ))}
        <polyline
          points={points.map((p) => `${p.x},${p.y}`).join(' ')}
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinejoin="round"
          opacity="0.5"
        />
        {points.map(({ a, x, y }) => (
          <circle
            key={a.id}
            cx={x}
            cy={y}
            r={a.id === activityId ? 5 : 4}
            fill={a.id === activityId ? 'currentColor' : 'var(--background)'}
            stroke="currentColor"
            strokeWidth="1.5"
          >
            <title>{`${date.format(new Date(a.startedAt))}: ${formatDuration(a.durationS)}`}</title>
          </circle>
        ))}
      </svg>
      <p
        className="mt-1 flex justify-between pl-[calc(100%*56/600)] text-xs text-muted-foreground tabular-nums"
        aria-hidden
      >
        <span>{date.format(first)}</span>
        {last > first && <span>{date.format(last)}</span>}
      </p>
    </figure>
  )
}
