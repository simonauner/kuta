import { isoWeek } from './activity.ts'

// Like the API: goals in SI units (meters, seconds) or number of activities. sportGroup null = all sports.
export const goalMetrics = ['distance', 'duration', 'count'] as const
export const goalPeriods = ['week', 'month', 'year'] as const
export const goalPrivacies = ['private', 'followers', 'public'] as const
export type GoalMetric = (typeof goalMetrics)[number]
export type GoalPeriod = (typeof goalPeriods)[number]
// Same choices and read rule as activity visibility; new goals are private.
export type GoalPrivacy = (typeof goalPrivacies)[number]
export type GoalPeriodProgress = { start: string; end: string; value: number; achieved: boolean }
export type Goal = {
  id: string
  sportGroup: string | null
  metric: GoalMetric
  period: GoalPeriod
  target: number
  recurring: boolean
  startsOn: string
  privacy: GoalPrivacy
  ended: boolean
  current: GoalPeriodProgress
  previous: GoalPeriodProgress | null
  streak: number
}

export function goalFraction(value: number, target: number) {
  return target > 0 ? Math.min(1, Math.max(0, value / target)) : 0
}

// Form units: km, minutes and whole activities.
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

// The period's name, regardless of when it is shown: "v. 44 2026", "oktober 2026", "2026".
export function goalPeriodLabel(period: GoalPeriod, start: string, locale?: string) {
  const date = localDate(start)
  if (period === 'week') {
    // The week-year follows the Thursday, like the ISO week: Monday 29 Dec 2025 is week 1 2026.
    const thursday = new Date(date.getFullYear(), date.getMonth(), date.getDate() + 3)
    return { week: isoWeek(date), year: thursday.getFullYear() }
  }
  if (period === 'month')
    return {
      month: new Intl.DateTimeFormat(locale, { month: 'long', year: 'numeric' }).format(date),
    }
  return { year: date.getFullYear() }
}

// Whole days left in the period, including today. end is the first day after the period.
export function goalDaysLeft(end: string, today: Date) {
  const [y, m, d] = end.split('-').map(Number)
  const days =
    (Date.UTC(y, m - 1, d) - Date.UTC(today.getFullYear(), today.getMonth(), today.getDate())) /
    86_400_000
  return Math.max(0, Math.round(days))
}
