import { isoWeek } from './activity.ts'
import sv from '../locales/sv.json' with { type: 'json' }

// Som API:t: mål i SI-enheter (meter, sekunder) eller antal pass. sportGroup null = alla träningsformer.
export const goalMetrics = ['distance', 'duration', 'count'] as const
export const goalPeriods = ['week', 'month', 'year'] as const
export type GoalMetric = (typeof goalMetrics)[number]
export type GoalPeriod = (typeof goalPeriods)[number]
export type GoalPeriodProgress = { start: string; end: string; value: number; achieved: boolean }
export type Goal = {
  id: string
  sportGroup: string | null
  metric: GoalMetric
  period: GoalPeriod
  target: number
  recurring: boolean
  startsOn: string
  ended: boolean
  current: GoalPeriodProgress
  previous: GoalPeriodProgress | null
  streak: number
}

// Från /api/goals/sport-groups: grupper som löpning (med terräng och virtuell löpning) och övriga sporter var för sig.
export type GoalSportGroup = { key: string; sports: string[] }
export type NamedSportGroup = keyof typeof sv.goals.sportGroups

// Grupper med flera sporter har egna namn; övriga grupper heter som sin sport.
export function isNamedSportGroup(key: string): key is NamedSportGroup {
  return Object.hasOwn(sv.goals.sportGroups, key)
}

export function goalFraction(value: number, target: number) {
  return target > 0 ? Math.min(1, Math.max(0, value / target)) : 0
}

// Formulärets enheter: km, minuter och hela pass.
export function goalInput(metric: GoalMetric, target: number) {
  const value = metric === 'distance' ? target / 1000 : metric === 'duration' ? target / 60 : target
  return String(Math.round(value * 1000) / 1000)
}

export function parseGoalInput(metric: GoalMetric, text: string) {
  const trimmed = text.trim().replace(',', '.')
  if (!/^\d+(\.\d+)?$/.test(trimmed)) return null
  const value = Number(trimmed)
  if (!(value > 0)) return null
  if (metric === 'count') return Number.isInteger(value) ? value : null
  return metric === 'distance' ? value * 1000 : Math.round(value * 60)
}

export function formatGoalAmount(metric: GoalMetric, value: number, locale?: string) {
  if (metric === 'distance') {
    const km = new Intl.NumberFormat(locale, { maximumFractionDigits: value < 10_000 ? 2 : 1 })
    return `${km.format(value / 1000)} km`
  }
  if (metric === 'duration') {
    const minutes = Math.floor(value / 60)
    const h = Math.floor(minutes / 60)
    const m = minutes % 60
    return h ? (m ? `${h} h ${m} min` : `${h} h`) : `${m} min`
  }
  return new Intl.NumberFormat(locale).format(value)
}

function localDate(date: string) {
  const [y, m, d] = date.split('-').map(Number)
  return new Date(y, m - 1, d)
}

// Periodens namn, oberoende av när den visas: "v. 44 2026", "oktober 2026", "2026".
export function goalPeriodLabel(period: GoalPeriod, start: string, locale?: string) {
  const date = localDate(start)
  if (period === 'week') {
    // Veckoåret följer torsdagen, som ISO-veckan: måndag 29 dec 2025 är v. 1 2026.
    const thursday = new Date(date.getFullYear(), date.getMonth(), date.getDate() + 3)
    return { week: isoWeek(date), year: thursday.getFullYear() }
  }
  if (period === 'month')
    return {
      month: new Intl.DateTimeFormat(locale, { month: 'long', year: 'numeric' }).format(date),
    }
  return { year: date.getFullYear() }
}

// Hela dagar kvar av perioden, inklusive i dag. end är periodens första dag efter slutet.
export function goalDaysLeft(end: string, today: Date) {
  const [y, m, d] = end.split('-').map(Number)
  const days =
    (Date.UTC(y, m - 1, d) - Date.UTC(today.getFullYear(), today.getMonth(), today.getDate())) /
    86_400_000
  return Math.max(0, Math.round(days))
}
