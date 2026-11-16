import { useMemo, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { HelpPopover } from '@/components/help-popover'
import { card } from '@/components/ui/card-variants'
import { Heading } from '@/components/ui/heading'
import { formatDuration, type ActivityStreams } from '@/lib/activity'
import { heartRateZones } from '@/lib/activity-analysis'

// From the lowest to the highest zone; more zones than colors reuse the top color.
const zoneColors = ['#adb5bd', '#4dabf7', '#40c057', '#fd7e14', '#fa5252']

// Time in each of the watch's heart rate zones, highest zone first. Hidden without zones or heart rate.
export function HeartRateZones({
  streams,
  bounds,
  sourceNote,
}: {
  streams: ActivityStreams | null
  bounds: number[] | null
  sourceNote: ReactNode
}) {
  const { t, i18n } = useTranslation()
  const seconds = useMemo(
    () => (streams && bounds ? heartRateZones(streams, bounds) : null),
    [streams, bounds],
  )
  if (!seconds || !bounds) return null
  const total = seconds.reduce((sum, s) => sum + s, 0)
  const percent = new Intl.NumberFormat(i18n.resolvedLanguage, { style: 'percent' })
  const zones = bounds.map((_, i) => i + 1).reverse()

  return (
    <section className={card({ className: 'mt-6' })} aria-labelledby="hr-zones-title">
      <div className="flex items-start justify-between gap-2">
        <Heading level={2} id="hr-zones-title">
          {t('hrZones.title')}
        </Heading>
        <HelpPopover label={t('hrZones.help')}>
          <p>{t('hrZones.helpText')}</p>
        </HelpPopover>
      </div>
      {sourceNote}
      <ul className="mt-3 grid gap-3">
        {zones.map((zone) => {
          const share = seconds[zone] / total
          return (
            <li key={zone}>
              <div className="flex flex-wrap items-baseline justify-between gap-x-4 text-sm">
                <span>
                  <span className="font-semibold">{t('hrZones.zone', { number: zone })}</span>{' '}
                  <span className="text-muted-foreground">
                    {zone === bounds.length
                      ? t('hrZones.above', { from: bounds[zone - 1] })
                      : t('hrZones.range', { from: bounds[zone - 1] + 1, to: bounds[zone] })}
                  </span>
                </span>
                <span className="tabular-nums">
                  {formatDuration(seconds[zone])} · {percent.format(share)}
                </span>
              </div>
              <div className="mt-1 h-3 overflow-hidden rounded-sm bg-muted" aria-hidden="true">
                <div
                  className="h-full"
                  style={{
                    width: `${share * 100}%`,
                    backgroundColor: zoneColors[Math.min(zone - 1, zoneColors.length - 1)],
                  }}
                />
              </div>
            </li>
          )
        })}
      </ul>
    </section>
  )
}
