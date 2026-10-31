import type { CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import {
  Activity,
  Accessibility,
  Bike,
  CircleDot,
  Dumbbell,
  Flag,
  Footprints,
  HeartPulse,
  Mountain,
  Music,
  PersonStanding,
  Sailboat,
  Ship,
  Snowflake,
  TrendingUp,
  Waves,
} from 'lucide-react'
import { knownSport, sportIcons } from '@/lib/sports'

const icons = {
  bike: Bike,
  swim: Waves,
  walk: Footprints,
  mountain: Mountain,
  snow: Snowflake,
  racket: CircleDot,
  ball: CircleDot,
  paddle: Ship,
  strength: Dumbbell,
  dance: Music,
  fitness: Activity,
  golf: Flag,
  sail: Sailboat,
  skate: Footprints,
  yoga: PersonStanding,
  health: HeartPulse,
  stairs: TrendingUp,
  wheelchair: Accessibility,
}

export function SportIcon({
  sport,
  className = 'size-6',
  style,
}: {
  sport: string
  className?: string
  style?: CSSProperties
}) {
  const { t } = useTranslation()
  const known = knownSport(sport)
  const label = known === 'other' && sport !== 'other' ? sport : t(`newActivity.sports.${known}`)
  const kind = sportIcons[known]
  const Icon = kind === 'run' ? null : icons[kind]
  return (
    <span
      style={style}
      role="img"
      aria-label={label}
      title={label}
      className={`inline-flex shrink-0 align-middle text-muted-foreground ${className}`}
    >
      {Icon ? (
        <Icon className="size-full" aria-hidden="true" />
      ) : (
        <svg
          className="size-full"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <circle cx="15" cy="4" r="2" />
          <path d="m12 8 4 4 4 1M12 8l-4 3-3-1m7-2-2 7 5 3 1 4m-6-7-4 5H2" />
        </svg>
      )}
    </span>
  )
}
