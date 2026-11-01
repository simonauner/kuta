import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import { Input } from '@/components/ui/input'
import { api } from '@/lib/api'
import { formatDuration } from '@/lib/activity'
import {
  loadChart,
  paceSeconds,
  parseThresholdPace,
  trainingLoadQueryKey,
  type TrainingLoadSummary,
} from '@/lib/training-load'

// "2026-05-03" is a local day; new Date() would parse it as UTC and could show the day before.
const localDay = (day: string) => new Date(`${day}T00:00`)

function useTrainingLoad() {
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  return useQuery({
    queryKey: trainingLoadQueryKey(timeZone),
    queryFn: () =>
      api.get<TrainingLoadSummary>(`/api/training-load?${new URLSearchParams({ timeZone })}`),
  })
}

// Warning at the top of Activities when fitness rises faster than the rule of thumb. Errors and loading aren't shown here.
export function TrainingLoadWarning() {
  const { t, i18n } = useTranslation()
  const load = useTrainingLoad()
  if (!load.data?.warning || load.data.rampPerWeek === null) return null
  const number = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 0 })
  return (
    <div
      role="status"
      className="mt-4 flex gap-3 rounded-xl border border-amber-500/50 bg-amber-500/10 p-4 text-sm"
    >
      <TriangleAlert className="mt-0.5 size-5 shrink-0 text-amber-600 dark:text-amber-400" />
      <div>
        <p className="font-medium">{t('trainingLoad.warningTitle')}</p>
        <p className="mt-1 text-muted-foreground">
          {t('trainingLoad.warningText', { ramp: number.format(load.data.rampPerWeek) })}{' '}
          <Link to="/app/stats" hash="training-load" className="underline underline-offset-2">
            {t('trainingLoad.warningLink')}
          </Link>
        </p>
      </div>
    </div>
  )
}

export function TrainingLoadSection() {
  const { t, i18n } = useTranslation()
  const load = useTrainingLoad()
  const number = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 0 })
  const signed = new Intl.NumberFormat(i18n.resolvedLanguage, {
    maximumFractionDigits: 0,
    signDisplay: 'exceptZero',
  })
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, { day: 'numeric', month: 'short' })
  const width = 600
  const height = 200
  const data = load.data
  const chart = data ? loadChart(data.days, { width, height, padding: 8 }) : null
  const today = data?.days.at(-1)

  return (
    <section id="training-load" className="mt-10 scroll-mt-4" aria-labelledby="training-load-title">
      <h2 id="training-load-title" className="text-xl font-semibold tracking-tight">
        {t('trainingLoad.title')}
      </h2>
      <p className="mt-2 text-sm text-muted-foreground">{t('trainingLoad.description')}</p>
      {load.error && (
        <p role="alert" className="mt-4 text-sm text-destructive">
          <ErrorMessage error={load.error} />{' '}
          <Button variant="outline" size="sm" onClick={() => void load.refetch()}>
            {t('activities.retry')}
          </Button>
        </p>
      )}
      {data && (
        <div className="motion-safe:animate-fade-in-up">
          <ThresholdForm summary={data} />
          {data.thresholdSpeedMS === null ? null : !today || !chart ? (
            <p className="mt-4 text-muted-foreground">{t('trainingLoad.empty')}</p>
          ) : (
            <>
              <dl className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
                {[
                  [t('trainingLoad.fitness'), number.format(today.fitness)],
                  [t('trainingLoad.fatigue'), number.format(today.fatigue)],
                  [t('trainingLoad.form'), signed.format(today.form)],
                  [
                    t('trainingLoad.ramp'),
                    data.rampPerWeek === null ? '–' : signed.format(data.rampPerWeek),
                  ],
                ].map(([label, value]) => (
                  <div key={label} className="rounded-xl border bg-card p-3 sm:p-4">
                    <dt className="text-xs text-muted-foreground">{label}</dt>
                    <dd className="mt-2 text-lg font-semibold tabular-nums sm:text-2xl">{value}</dd>
                  </div>
                ))}
              </dl>
              <div className="mt-4 rounded-xl border bg-card p-4">
                <p className="text-xs text-muted-foreground">
                  {t('trainingLoad.chartScale', { max: number.format(chart.maximum) })}
                </p>
                <svg
                  viewBox={`0 0 ${width} ${height}`}
                  className="mt-2 w-full"
                  role="img"
                  aria-label={t('trainingLoad.chart', {
                    from: date.format(localDay(data.days[0].day)),
                    to: date.format(localDay(today.day)),
                  })}
                >
                  <g className="text-muted-foreground" fill="currentColor" opacity="0.35">
                    {chart.bars
                      .filter((bar) => bar.height > 0)
                      .map((bar) => (
                        <rect
                          key={bar.day}
                          x={bar.x}
                          y={bar.y}
                          width={bar.width}
                          height={bar.height}
                        >
                          <title>{`${date.format(localDay(bar.day))}: ${number.format(bar.load)}`}</title>
                        </rect>
                      ))}
                  </g>
                  <polyline
                    points={chart.fatigue}
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="1.5"
                    strokeDasharray="4 3"
                    className="text-muted-foreground"
                  />
                  <polyline
                    points={chart.fitness}
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2.5"
                    className="text-primary"
                  />
                </svg>
                <p className="mt-1 flex justify-between text-xs text-muted-foreground tabular-nums">
                  <span>{date.format(localDay(data.days[0].day))}</span>
                  <span>{date.format(localDay(today.day))}</span>
                </p>
                <p className="mt-2 flex flex-wrap gap-4 text-xs text-muted-foreground">
                  <span className="inline-flex items-center gap-1.5">
                    <span className="h-0.5 w-4 bg-primary" aria-hidden />
                    {t('trainingLoad.fitness')}
                  </span>
                  <span className="inline-flex items-center gap-1.5">
                    <span
                      className="w-4 border-t-[1.5px] border-dashed border-muted-foreground"
                      aria-hidden
                    />
                    {t('trainingLoad.fatigue')}
                  </span>
                  <span className="inline-flex items-center gap-1.5">
                    <span className="size-2.5 bg-muted-foreground/35" aria-hidden />
                    {t('trainingLoad.dailyLoad')}
                  </span>
                </p>
              </div>
            </>
          )}
          <p className="mt-4 text-xs leading-relaxed text-muted-foreground">
            {t('trainingLoad.method')}
          </p>
        </div>
      )}
    </section>
  )
}

function ThresholdForm({ summary }: { summary: TrainingLoadSummary }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const manual = summary.thresholdSource === 'manual' ? summary.thresholdSpeedMS : null
  const [text, setText] = useState(manual ? formatDuration(paceSeconds(manual)) : '')
  const [invalid, setInvalid] = useState(false)
  const save = useMutation({
    mutationFn: (thresholdSpeedMS: number | null) =>
      api.send('PUT', '/api/me/threshold-speed', { thresholdSpeedMS }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['activities', 'training-load'] }),
  })
  const submit = (event: FormEvent) => {
    event.preventDefault()
    const speed = text.trim() === '' ? null : parseThresholdPace(text)
    setInvalid(speed === null && text.trim() !== '')
    if (speed !== null || text.trim() === '') save.mutate(speed)
  }
  const predicted = summary.predictedThresholdSpeedMS
  return (
    <form onSubmit={submit} className="mt-4 rounded-xl border bg-card p-4" noValidate>
      <FormField label={t('trainingLoad.threshold')}>
        {(id) => (
          <>
            <p id={`${id}-hint`} className="mb-3 text-xs text-muted-foreground">
              {summary.thresholdSource === 'manual'
                ? t('trainingLoad.thresholdManual')
                : predicted
                  ? t('trainingLoad.thresholdPredicted', {
                      pace: formatDuration(paceSeconds(predicted)),
                    })
                  : t('trainingLoad.thresholdMissing')}
            </p>
            <div className="flex flex-wrap items-center gap-2">
              <Input
                id={id}
                inputMode="numeric"
                className="w-28"
                placeholder={predicted ? formatDuration(paceSeconds(predicted)) : '4:30'}
                value={text}
                aria-invalid={invalid}
                aria-describedby={invalid ? `${id}-hint ${id}-error` : `${id}-hint`}
                onChange={(event) => setText(event.target.value)}
              />
              <span className="text-sm text-muted-foreground">/km</span>
              <Button type="submit" size="sm" disabled={save.isPending}>
                {t('trainingLoad.save')}
              </Button>
              {summary.thresholdSource === 'manual' && (
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  disabled={save.isPending}
                  onClick={() => {
                    setText('')
                    setInvalid(false)
                    save.mutate(null)
                  }}
                >
                  {t('trainingLoad.useAutomatic')}
                </Button>
              )}
            </div>
            {invalid && (
              <p id={`${id}-error`} role="alert" className="mt-2 text-sm text-destructive">
                {t('errors.invalidThresholdSpeed')}
              </p>
            )}
          </>
        )}
      </FormField>
      {save.error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          <ErrorMessage error={save.error} />
        </p>
      )}
    </form>
  )
}
