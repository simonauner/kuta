import { Award, Medal, Trophy } from 'lucide-react'
import { useId, useState } from 'react'
import { Tooltip } from '@base-ui/react/tooltip'
import { useTranslation } from 'react-i18next'
import type { ActivityBestEffort } from '@/lib/records'
import './record-award.css'

const metals = {
  gold: ['#a86610', '#ffcc4d', '#fff6c9', '#e9aa23', '#ffd66b'],
  silver: ['#718296', '#c6d2df', '#ffffff', '#a8b8cb', '#e2eaf2'],
  bronze: ['#7a3f17', '#c98245', '#ffe2c4', '#a65e2a', '#dd9a5e'],
}

// Fyllning med glansrand för ikoner i rekordens och supporterbadgens metaller.
export function MetalGradient({ id, metal }: { id: string; metal: keyof typeof metals }) {
  const colors = metals[metal]
  return (
    <defs>
      <linearGradient id={id} x1="0" y1="0" x2="0.8" y2="1">
        <stop offset="0%" stopColor={colors[0]} />
        <stop offset="30%" stopColor={colors[1]} />
        <stop offset="46%" stopColor={colors[2]} />
        <stop offset="58%" stopColor={colors[3]} />
        <stop offset="80%" stopColor={colors[4]} />
        <stop offset="100%" stopColor={colors[0]} />
      </linearGradient>
    </defs>
  )
}

type RecordAwardProps =
  { status: NonNullable<ActivityBestEffort['recordStatus']> } | { status: 'year'; year: number }

export function RecordAward(props: RecordAwardProps) {
  const { status } = props
  const { t } = useTranslation()
  const gradientId = useId()
  const [open, setOpen] = useState(false)
  const label =
    props.status === 'year'
      ? t('records.yearBest', { year: props.year })
      : t(status === 'current' ? 'records.currentBest' : 'records.previousBest')
  const metal = status === 'current' ? 'gold' : 'silver'
  const Icon = status === 'current' ? Trophy : status === 'year' ? Award : Medal
  return (
    <Tooltip.Root open={open} onOpenChange={setOpen}>
      <Tooltip.Trigger
        type="button"
        aria-label={label}
        className={`record-award record-award--${status === 'year' ? 'outline' : metal}`}
        delay={150}
        closeOnClick={false}
        onClick={() => setOpen(true)}
      >
        <Icon
          className="size-6"
          fill={status === 'year' ? 'none' : `url(#${gradientId})`}
          strokeWidth={1.6}
          aria-hidden="true"
        >
          {status !== 'year' && <MetalGradient id={gradientId} metal={metal} />}
        </Icon>
      </Tooltip.Trigger>
      <Tooltip.Portal>
        <Tooltip.Positioner sideOffset={8} className="z-50">
          <Tooltip.Popup className="rounded-md border bg-foreground px-3 py-2 text-xs font-medium text-background shadow-lg">
            {label}
          </Tooltip.Popup>
        </Tooltip.Positioner>
      </Tooltip.Portal>
    </Tooltip.Root>
  )
}
