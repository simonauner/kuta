import { Stat, StatHint } from '@/components/stat'
import { useMemo, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { HelpPopover } from '@/components/help-popover'
import { card } from '@/components/ui/card-variants'
import { Heading } from '@/components/ui/heading'
import { formatDuration, formatPace, type ActivityLap, type ActivityStreams } from '@/lib/activity'
import { heartRateDrift, intervals, pacing } from '@/lib/activity-analysis'

// Fixed rules for one running activity (lib/activity-analysis.ts). Hidden when no rule applies.
export function ActivityAnalysis({
  streams,
  laps,
  sourceNote,
}: {
  streams: ActivityStreams | null
  laps: ActivityLap[]
  sourceNote: ReactNode
}) {
  const { t, i18n } = useTranslation()
  const result = useMemo(
    () => ({
      pacing: streams ? pacing(streams) : null,
      drift: streams ? heartRateDrift(streams) : null,
      intervals: intervals(laps),
    }),
    [streams, laps],
  )
  if (!result.pacing && !result.drift && !result.intervals) return null
  const percent = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 1 })
  const meters = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 0 })
  const { pacing: split, drift, intervals: reps } = result
  // Rep distances within 5 % of each other are described as one distance ("4 × 1 000 m")
  const sameDistance =
    reps &&
    Math.max(...reps.reps.map((r) => r.distanceM)) <=
      Math.min(...reps.reps.map((r) => r.distanceM)) * 1.05

  return (
    <section className={card({ className: 'mt-6' })} aria-labelledby="activity-analysis-title">
      <div className="flex items-start justify-between gap-2">
        <Heading level={2} id="activity-analysis-title">
          {t('analysis.title')}
        </Heading>
        <HelpPopover label={t('analysis.help')}>
          <p>{t('analysis.helpPacing')}</p>
          <p className="mt-2">{t('analysis.helpDrift')}</p>
          <p className="mt-2">{t('analysis.helpIntervals')}</p>
        </HelpPopover>
      </div>
      {sourceNote}
      <dl className="mt-3 grid gap-4 sm:grid-cols-2">
        {split && (
          <Stat label={t('analysis.pacing')} value={t(`analysis.split.${split.split}`)}>
            <StatHint>
              {t('analysis.halves', {
                first: formatDuration(Math.round(split.firstHalfS)),
                second: formatDuration(Math.round(split.secondHalfS)),
              })}
              {split.split !== 'even' &&
                ` · ${t(split.percent < 0 ? 'analysis.secondFaster' : 'analysis.secondSlower', {
                  percent: percent.format(Math.abs(split.percent)),
                })}`}
            </StatHint>
            {Math.abs(split.finishPercent) >= 5 && (
              <StatHint>
                {t(split.finishPercent > 0 ? 'analysis.finishSlower' : 'analysis.finishFaster', {
                  percent: percent.format(Math.abs(split.finishPercent)),
                })}
              </StatHint>
            )}
          </Stat>
        )}
        {drift && (
          <Stat
            label={t('analysis.drift')}
            value={`${percent.format(Math.max(0, drift.percent))} %`}
          >
            <StatHint>{t(`analysis.driftLevel.${drift.level}`)}</StatHint>
          </Stat>
        )}
      </dl>
      {reps && (
        <div className="mt-4">
          <Heading level={3}>
            {sameDistance
              ? t('analysis.repsSame', {
                  count: reps.reps.length,
                  distance: meters.format(
                    Math.round(
                      reps.reps.reduce((sum, r) => sum + r.distanceM, 0) / reps.reps.length / 10,
                    ) * 10,
                  ),
                })
              : t('analysis.reps', { count: reps.reps.length })}
          </Heading>
          <p className="mt-1 text-sm text-muted-foreground tabular-nums">
            {t('analysis.repsSummary', {
              average: formatDuration(Math.round(reps.averagePace)),
              fastest: formatDuration(Math.round(reps.fastestPace)),
              slowest: formatDuration(Math.round(reps.slowestPace)),
            })}{' '}
            {Math.abs(reps.lastPercent) < 1
              ? t('analysis.lastEven')
              : t(reps.lastPercent > 0 ? 'analysis.lastSlower' : 'analysis.lastFaster', {
                  percent: percent.format(Math.abs(reps.lastPercent)),
                })}
          </p>
          <div className="mt-2 overflow-x-auto">
            <table className="w-full text-sm whitespace-nowrap tabular-nums [&_td]:pr-4 [&_th]:pr-4">
              <thead className="text-left text-xs text-muted-foreground">
                <tr>
                  <th className="py-1 font-normal">{t('analysis.rep')}</th>
                  <th className="py-1 font-normal">{t('activity.distance')}</th>
                  <th className="py-1 font-normal">{t('activity.time')}</th>
                  <th className="py-1 font-normal">{t('activity.pace')}</th>
                  {reps.reps.some((r) => r.avgHr !== null) && (
                    <th className="py-1 font-normal">{t('activity.splitHr')}</th>
                  )}
                  <th className="py-1 font-normal">{t('analysis.recovery')}</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {reps.reps.map((r) => (
                  <tr key={r.number}>
                    <td className="py-1.5">{r.number}</td>
                    <td className="py-1.5">{meters.format(r.distanceM)} m</td>
                    <td className="py-1.5">{formatDuration(Math.round(r.durationS))}</td>
                    <td className="py-1.5">{formatPace(r.durationS, r.distanceM)}</td>
                    {reps.reps.some((x) => x.avgHr !== null) && (
                      <td className="py-1.5">{r.avgHr ?? '–'}</td>
                    )}
                    <td className="py-1.5">
                      {r.recoveryS === null ? '–' : formatDuration(Math.round(r.recoveryS))}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </section>
  )
}
