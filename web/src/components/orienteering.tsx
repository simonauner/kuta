import { useTranslation } from 'react-i18next'
import { HelpPopover } from '@/components/help-popover'
import { Input } from '@/components/ui/input'
import { card } from '@/components/ui/card-variants'
import { FormField } from '@/components/ui/form-field'
import { Heading } from '@/components/ui/heading'
import { formatDuration, type ActivityDetail } from '@/lib/activity'
import { orienteeringLegs } from '@/lib/orienteering'

export function CourseLengthField({
  value,
  onChange,
  disabled,
  className,
}: {
  value: string
  onChange: (km: string) => void
  disabled?: boolean
  className?: string
}) {
  const { t } = useTranslation()
  return (
    <div className={`space-y-2 ${className ?? ''}`}>
      <FormField label={t('orienteering.courseLength')}>
        {(id) => (
          <Input
            id={id}
            type="number"
            inputMode="decimal"
            min={0.01}
            max={100}
            step={0.01}
            className="w-32"
            value={value}
            onChange={(event) => onChange(event.currentTarget.value)}
            disabled={disabled}
          />
        )}
      </FormField>
      <p className="text-xs text-muted-foreground">{t('orienteering.courseLengthHint')}</p>
    </div>
  )
}

// Legs from the watch's laps, with probable misses where GPS gives the straight line (only for the owner).
export function OrienteeringLegs({ activity, own }: { activity: ActivityDetail; own: boolean }) {
  const { t, i18n } = useTranslation()
  if (activity.laps.length < 2)
    return own ? (
      <p className="mt-6 text-sm text-muted-foreground">{t('orienteering.noLegs')}</p>
    ) : null
  const legs = orienteeringLegs(activity.laps, activity.streams)
  const number = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 0 })
  const percent = new Intl.NumberFormat(i18n.resolvedLanguage, {
    style: 'percent',
    signDisplay: 'always',
    maximumFractionDigits: 0,
  })
  const straight = legs.some((l) => l.straightM !== null)
  const still = legs.some((l) => l.stillS !== null)
  return (
    <section className={card({ className: 'mt-6' })} aria-labelledby="legs-title">
      <div className="flex items-center gap-1">
        <Heading level={2} id="legs-title">
          {t('orienteering.legs')}
        </Heading>
        <HelpPopover label={t('orienteering.legsHelpLabel')}>
          {t('orienteering.legsHelp')}
          {straight && ` ${t('orienteering.missHelp')}`}
        </HelpPopover>
      </div>
      <div className="mt-3 overflow-x-auto">
        <table className="w-full text-sm whitespace-nowrap tabular-nums">
          <thead className="text-left text-xs text-muted-foreground">
            <tr>
              <th className="py-1 pr-3 font-normal">{t('orienteering.leg')}</th>
              <th className="py-1 pr-3 font-normal">{t('activity.time')}</th>
              <th className="py-1 pr-3 font-normal">{t('orienteering.run')}</th>
              {straight && (
                <>
                  <th className="py-1 pr-3 font-normal">{t('orienteering.straight')}</th>
                  <th className="py-1 pr-3 font-normal">{t('orienteering.overdistance')}</th>
                </>
              )}
              {still && <th className="py-1 font-normal">{t('orienteering.still')}</th>}
            </tr>
          </thead>
          <tbody>
            {legs.map((leg) => (
              <tr key={leg.number}>
                <td className="py-1.5 pr-3">
                  {leg.number}
                  {leg.miss && (
                    <span className="ml-2 rounded-full border border-amber-500/50 bg-amber-500/10 px-2 py-0.5 text-xs">
                      {t('orienteering.miss')}
                    </span>
                  )}
                </td>
                <td className="py-1.5 pr-3">{formatDuration(leg.durationS)}</td>
                <td className="py-1.5 pr-3">{number.format(leg.distanceM)} m</td>
                {straight && (
                  <>
                    <td className="py-1.5 pr-3">
                      {leg.straightM === null ? '–' : `${number.format(leg.straightM)} m`}
                    </td>
                    <td className="py-1.5 pr-3">
                      {leg.straightM ? percent.format(leg.distanceM / leg.straightM - 1) : '–'}
                    </td>
                  </>
                )}
                {still && (
                  <td className="py-1.5">
                    {leg.stillS === null ? '–' : formatDuration(leg.stillS)}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}
