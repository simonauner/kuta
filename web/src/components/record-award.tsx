import { Medal, Trophy, type LucideIcon } from 'lucide-react'
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

// Fill with a sheen stripe for icons in the records' and supporter badge's metals.
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

type RecordStatus = NonNullable<ActivityBestEffort['recordStatus']>

// The top three get filled trophies in their metal, the year's top three medals; a previous personal best keeps the
// gold trophy as an outline.
const looks = {
  current: { Icon: Trophy, metal: 'gold', filled: true },
  second: { Icon: Trophy, metal: 'silver', filled: true },
  third: { Icon: Trophy, metal: 'bronze', filled: true },
  previous: { Icon: Trophy, metal: 'gold', filled: false },
  year: { Icon: Medal, metal: 'gold', filled: true },
  yearSecond: { Icon: Medal, metal: 'silver', filled: true },
  yearThird: { Icon: Medal, metal: 'bronze', filled: true },
} as const satisfies Record<
  RecordStatus,
  { Icon: LucideIcon; metal: keyof typeof metals; filled: boolean }
>

function RecordIcon({ status }: { status: RecordStatus }) {
  const gradientId = useId()
  const { Icon, metal, filled } = looks[status]
  return (
    <Icon
      className="size-6"
      fill={filled ? `url(#${gradientId})` : 'none'}
      strokeWidth={filled ? 1.6 : 2}
      aria-hidden="true"
    >
      {filled && <MetalGradient id={gradientId} metal={metal} />}
    </Icon>
  )
}

const tone = (status: RecordStatus) => `record-award--${looks[status].metal}`

// The same award without a tooltip, next to text that already names it (the feed)
export function RecordMedal({ status }: { status: RecordStatus }) {
  return (
    <span aria-hidden="true" className={`record-medal ${tone(status)}`}>
      <RecordIcon status={status} />
    </span>
  )
}

// year: the UTC year of the activity, named by the year places' labels
export function RecordAward({ status, year }: { status: RecordStatus; year: number }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const label = t(`records.${status}Best`, { year })
  return (
    <Tooltip.Root open={open} onOpenChange={setOpen}>
      <Tooltip.Trigger
        type="button"
        aria-label={label}
        className={`record-award ${tone(status)}`}
        delay={150}
        closeOnClick={false}
        onClick={() => setOpen(true)}
      >
        <RecordIcon status={status} />
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
