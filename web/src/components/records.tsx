import { FormField } from '@/components/ui/form-field'
import { Heading } from '@/components/ui/heading'
import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { HelpPopover } from '@/components/help-popover'
import { ErrorMessage } from '@/components/error-message'
import { RecordAward } from '@/components/record-award'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import { formatDuration, formatPace } from '@/lib/activity'
import {
  recordDistances,
  recordMeters,
  trendChart,
  type RacePredictions,
  type RecordDistance,
  type RecordTrend,
  type StoredBestEffort,
} from '@/lib/records'
import { Card } from '@/components/ui/card'
import { Select } from '@/components/ui/select'

function QueryError({ error, retry }: { error: Error; retry: () => void }) {
  const { t } = useTranslation()
  return (
    <p role="alert" className="mt-4 text-sm text-destructive">
      <ErrorMessage error={error} />{' '}
      <Button variant="secondary-outline" size="sm" onClick={retry}>
        {t('activities.retry')}
      </Button>
    </p>
  )
}

function EffortLink({ effort }: { effort: { activityId: string; elapsedS: number } }) {
  return (
    <Link
      to="/app/activities/$activityId"
      params={{ activityId: effort.activityId }}
      className="font-medium underline-offset-2 underline"
    >
      {formatDuration(effort.elapsedS)}
    </Link>
  )
}

// Running records: personal and yearly bests, progress per distance and prediction (VDOT).
export function RecordsSection({ year }: { year: number }) {
  const { t, i18n } = useTranslation()
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const [distance, setDistance] = useState<RecordDistance>(5000)
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })
  const distanceLabel = (d: number) => t(`records.distances.${d as RecordDistance}`)
  const allTime = useQuery({
    queryKey: ['records'],
    queryFn: () => api.get<StoredBestEffort[]>('/api/records'),
  })
  const yearly = useQuery({
    queryKey: ['records', 'year', year],
    queryFn: () => api.get<StoredBestEffort[]>(`/api/records?year=${year}`),
  })
  const trend = useQuery({
    queryKey: ['records', 'trend', distance, timeZone],
    queryFn: () =>
      api.get<RecordTrend>(
        `/api/records/trend?${new URLSearchParams({ distanceM: String(distance), timeZone })}`,
      ),
  })
  const predictions = useQuery({
    queryKey: ['records', 'predictions'],
    queryFn: () => api.get<RacePredictions>('/api/records/predictions'),
  })
  const best = (list: StoredBestEffort[] | undefined, d: number) =>
    list?.find((e) => e.distanceM === d)

  return (
    <section className="mt-10" aria-labelledby="records-title">
      <div className="flex items-center justify-between gap-4">
        <Heading level={2} id="records-title">
          {t('records.title')}
        </Heading>
        <HelpPopover label={t('records.helpLabel')}>{t('records.help')}</HelpPopover>
      </div>
      <p className="mt-2 text-body text-muted-foreground">{t('records.description')}</p>

      {allTime.error && <QueryError error={allTime.error} retry={() => void allTime.refetch()} />}
      {yearly.error && <QueryError error={yearly.error} retry={() => void yearly.refetch()} />}
      {allTime.isSuccess &&
        (allTime.data.length === 0 ? (
          <p className="mt-4 text-muted-foreground motion-safe:animate-fade-in-up">
            {t('records.empty')}
          </p>
        ) : (
          <div className="mt-4 overflow-x-auto motion-safe:animate-fade-in-up">
            <table className="w-full text-right text-sm tabular-nums">
              <thead className="text-xs text-muted-foreground">
                <tr>
                  <th scope="col" className="py-3 pr-3 text-left">
                    {t('records.distance')}
                  </th>
                  <th scope="col" className="px-2">
                    {t('records.personalBest')}
                  </th>
                  <th scope="col" className="px-2">
                    {t('records.yearBest', { year })}
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {recordDistances.map(([d, meters]) => {
                  const pb = best(allTime.data, d)
                  const yb = best(yearly.data, d)
                  return (
                    <tr key={d}>
                      <th scope="row" className="py-3 pr-3 text-left font-medium">
                        {distanceLabel(d)}
                      </th>
                      {[pb, yb].map((effort, i) => (
                        <td key={i} className="px-2 py-3 align-top">
                          {effort ? (
                            <>
                              <EffortLink effort={effort} />
                              <span className="block text-xs text-muted-foreground">
                                {formatPace(effort.elapsedS, meters)} ·{' '}
                                {date.format(new Date(effort.achievedAt))}
                              </span>
                            </>
                          ) : (
                            '–'
                          )}
                        </td>
                      ))}
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        ))}

      <Card className="mt-6">
        <Heading level={3} className="mb-3">
          {t('records.trend')}
        </Heading>
        <FormField
          layout="inline"
          label={t('records.distance')}
          className="justify-between text-sm font-medium"
        >
          {(id) => (
            <Select
              id={id}
              className="w-fit"
              value={distance}
              onChange={(e) => setDistance(Number(e.target.value) as RecordDistance)}
            >
              {recordDistances.map(([d]) => (
                <option key={d} value={d}>
                  {distanceLabel(d)}
                </option>
              ))}
            </Select>
          )}
        </FormField>
        {trend.error && <QueryError error={trend.error} retry={() => void trend.refetch()} />}
        {trend.isSuccess && (
          <div className="motion-safe:animate-fade-in-up">
            <RecordTrendView trend={trend.data} distanceLabel={distanceLabel(distance)} />
          </div>
        )}
      </Card>

      <Card className="mt-6">
        <div className="flex items-center justify-between gap-4">
          <Heading level={3}>{t('records.predictions')}</Heading>
          <HelpPopover label={t('records.predictionsMethodLabel')}>
            {t('records.predictionsMethod')}
          </HelpPopover>
        </div>
        {predictions.error && (
          <QueryError error={predictions.error} retry={() => void predictions.refetch()} />
        )}
        {predictions.isSuccess && (
          <div className="motion-safe:animate-fade-in-up">
            {predictions.data.basis === null || predictions.data.vdot === null ? (
              <p className="mt-3 text-sm text-muted-foreground">{t('records.predictionsEmpty')}</p>
            ) : (
              <>
                <p className="mt-2 text-body text-muted-foreground">
                  {t('records.predictionsBasis', {
                    distance: distanceLabel(predictions.data.basis.distanceM),
                    time: formatDuration(predictions.data.basis.elapsedS),
                    date: date.format(new Date(predictions.data.basis.achievedAt)),
                    vdot: new Intl.NumberFormat(i18n.resolvedLanguage, {
                      maximumFractionDigits: 1,
                    }).format(predictions.data.vdot),
                  })}{' '}
                  <Link
                    to="/app/activities/$activityId"
                    params={{ activityId: predictions.data.basis.activityId }}
                    className="underline underline-offset-2"
                  >
                    {t('records.showActivity')}
                  </Link>
                </p>
                <dl className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3">
                  {predictions.data.predictions.map((p) => (
                    <Card key={p.distanceM} padding="compact">
                      <dt className="text-xs text-muted-foreground">
                        {distanceLabel(p.distanceM)}
                      </dt>
                      <dd className="mt-1 text-lg font-semibold tabular-nums">
                        {formatDuration(p.elapsedS)}
                      </dd>
                      <dd className="text-xs text-muted-foreground tabular-nums">
                        {formatPace(p.elapsedS, recordMeters(p.distanceM))}
                      </dd>
                    </Card>
                  ))}
                </dl>
              </>
            )}
          </div>
        )}
      </Card>
    </section>
  )
}

function RecordTrendView({ trend, distanceLabel }: { trend: RecordTrend; distanceLabel: string }) {
  const { t, i18n } = useTranslation()
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })
  const width = 600
  const height = 200
  const chart = trendChart(trend, { width, height, padding: 12 })
  if (!chart) {
    return (
      <p className="mt-3 text-sm text-muted-foreground">
        {t('records.trendEmpty', { distance: distanceLabel })}
      </p>
    )
  }
  const history = [...trend.progression].reverse()
  return (
    <>
      <p className="mt-3 text-xs text-muted-foreground">
        {t('records.chartScale', {
          best: formatDuration(chart.fastest),
          worst: formatDuration(chart.slowest),
        })}{' '}
        {t('records.chartScaleHint')}
      </p>
      <svg
        viewBox={`0 0 ${width} ${height}`}
        className="mt-2 w-full overflow-visible text-primary"
        role="img"
        aria-label={t('records.trendChart', { distance: distanceLabel })}
      >
        <polyline
          points={chart.steps}
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinejoin="round"
        />
        {chart.months.map((p) => (
          <circle
            key={`${p.activityId}-${p.achievedAt}`}
            cx={p.x}
            cy={p.y}
            r="4"
            fill="var(--card)"
            stroke="currentColor"
            strokeWidth="1.5"
            opacity="0.7"
          >
            <title>{`${date.format(new Date(p.achievedAt))}: ${formatDuration(p.elapsedS)}`}</title>
          </circle>
        ))}
        {chart.records.map((p) => (
          <circle
            key={`${p.activityId}-${p.achievedAt}`}
            cx={p.x}
            cy={p.y}
            r="5"
            fill="currentColor"
          >
            <title>{`${t('records.personalBest')} ${date.format(new Date(p.achievedAt))}: ${formatDuration(p.elapsedS)}`}</title>
          </circle>
        ))}
      </svg>
      <p
        className="mt-1 flex justify-between text-xs text-muted-foreground tabular-nums"
        aria-hidden
      >
        <span>{date.format(chart.first)}</span>
        {chart.last > chart.first && <span>{date.format(chart.last)}</span>}
      </p>
      <p className="mt-2 flex flex-wrap gap-4 text-xs text-muted-foreground">
        <span className="inline-flex items-center gap-1.5">
          <span className="size-2.5 rounded-full border-[1.5px] border-primary" aria-hidden />
          {t('records.legendMonth')}
        </span>
        <span className="inline-flex items-center gap-1.5">
          <span className="size-2.5 rounded-full bg-primary" aria-hidden />
          {t('records.legendRecord')}
        </span>
      </p>
      <div className="mt-4 overflow-x-auto">
        <table className="w-full text-right text-sm tabular-nums">
          <caption className="pb-3 text-left font-medium">
            {t('records.history', { distance: distanceLabel })}
          </caption>
          <thead className="text-xs text-muted-foreground">
            <tr>
              <th scope="col" className="py-2 pr-3 text-left">
                {t('records.date')}
              </th>
              <th scope="col" className="px-2">
                {t('records.time')}
              </th>
              <th scope="col" className="px-2">
                {t('records.improvement')}
              </th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {history.map((effort, i) => {
              const previous = history[i + 1]
              return (
                <tr key={`${effort.activityId}-${effort.achievedAt}`}>
                  <th scope="row" className="py-2 pr-3 text-left font-normal">
                    <span className="inline-flex items-center gap-2">
                      <RecordAward status={i === 0 ? 'current' : 'previous'} />
                      <Link
                        to="/app/activities/$activityId"
                        params={{ activityId: effort.activityId }}
                        className="underline-offset-2 underline"
                      >
                        {date.format(new Date(effort.achievedAt))}
                      </Link>
                    </span>
                  </th>
                  <td className="px-2">
                    <EffortLink effort={effort} />
                  </td>
                  <td className="px-2 text-muted-foreground">
                    {previous ? `−${formatDuration(previous.elapsedS - effort.elapsedS)}` : '–'}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </>
  )
}
