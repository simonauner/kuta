export const rollingPeriods = ['4w', '12w', '26w', '12m'] as const
export type RollingPeriod = (typeof rollingPeriods)[number]
// A rolling period ending today, or a calendar year.
export type StatisticsPeriod = RollingPeriod | number

export type StatisticsBucket = 'week' | 'month'
// Local dates [from, to) as YYYY-MM-DD, matching GET /api/activities/statistics.
export type StatisticsRange = { from: string; to: string; bucket: StatisticsBucket }

const weeks: Partial<Record<RollingPeriod, number>> = { '4w': 4, '12w': 12, '26w': 26 }

const iso = (date: Date) => date.toISOString().slice(0, 10)
const utc = (year: number, month: number, day: number) => new Date(Date.UTC(year, month, day))

// The chosen period and the period before it, which covers the same span so a partial week, month or year compares fairly.
// Weeks start on Monday like the API's ISO weeks; today is the local date.
export function statisticsRanges(
  period: StatisticsPeriod,
  today: { year: number; month: number; day: number },
): { current: StatisticsRange; previous: StatisticsRange } {
  const { year, month, day } = today
  const tomorrow = utc(year, month - 1, day + 1)
  const weekCount = typeof period === 'number' ? undefined : weeks[period]
  if (weekCount) {
    const weekday = (utc(year, month - 1, day).getUTCDay() + 6) % 7
    const from = utc(year, month - 1, day - weekday - (weekCount - 1) * 7)
    const days = (tomorrow.getTime() - from.getTime()) / 86_400_000
    const previousFrom = utc(from.getUTCFullYear(), from.getUTCMonth(), from.getUTCDate() - days)
    return {
      current: { from: iso(from), to: iso(tomorrow), bucket: 'week' },
      previous: { from: iso(previousFrom), to: iso(from), bucket: 'week' },
    }
  }
  const [from, to, months] =
    typeof period === 'number'
      ? [utc(period, 0, 1), period < year ? utc(period + 1, 0, 1) : tomorrow, 12]
      : [utc(year, month - 12, 1), tomorrow, 12]
  const shift = (date: Date) =>
    utc(date.getUTCFullYear(), date.getUTCMonth() - months, date.getUTCDate())
  return {
    current: { from: iso(from), to: iso(to), bucket: 'month' },
    previous: { from: iso(shift(from)), to: iso(shift(to)), bucket: 'month' },
  }
}

// Relative change in percent, or null when there is nothing to compare with.
export function change(current: number, previous: number) {
  return previous > 0 ? ((current - previous) / previous) * 100 : null
}

// Indexes of consecutive values, split where a value is missing, so a line breaks instead of dropping to zero.
export function lineRuns(values: (number | null)[]) {
  const runs: number[][] = []
  values.forEach((value, index) => {
    if (value === null) return
    const last = runs[runs.length - 1]
    if (last && last[last.length - 1] === index - 1) last.push(index)
    else runs.push([index])
  })
  return runs
}

// Round steps per metric in its stored unit (meters, seconds, seconds per km, bpm).
const axisSteps: Record<'distanceM' | 'durationS' | 'paceSPerKm' | 'avgHr', number[]> = {
  distanceM: [1, 2, 2.5, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000].map((km) => km * 1000),
  durationS: [0.25, 0.5, 1, 2, 5, 10, 20, 50, 100].map((h) => h * 3600),
  paceSPerKm: [5, 10, 15, 30, 60, 120, 300, 600],
  avgHr: [1, 2, 5, 10, 20, 50],
}

// Gridlines for a chart: sums start at zero, averages at a round value below the lowest one so their variation shows.
export function axisTicks(metric: keyof typeof axisSteps, values: (number | null)[], lines = 4) {
  const present = values.filter((v): v is number => v !== null)
  const zeroBased = metric === 'distanceM' || metric === 'durationS'
  const low = zeroBased || present.length === 0 ? 0 : Math.min(...present)
  const high = Math.max(low, ...present)
  // The step with the least headroom above the data, at most `lines` intervals; on a tie the finer one.
  let best = { lower: 0, upper: Infinity, step: 1 }
  for (const step of axisSteps[metric]) {
    const lower = Math.floor(low / step) * step
    const upper = Math.max(lower + 2 * step, Math.ceil(high / step) * step)
    if ((upper - lower) / step <= lines && upper - lower < best.upper - best.lower)
      best = { lower, upper, step }
  }
  const { lower, upper, step } = best
  return Array.from({ length: Math.round((upper - lower) / step) + 1 }, (_, i) => lower + i * step)
}
