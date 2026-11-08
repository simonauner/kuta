// Steady runs from GET /api/aerobic-efficiency: grade-adjusted speed and heart rate after the warm-up.
export type AerobicPoint = { activityId: string; day: string; speedMS: number; hr: number }

export type AerobicMonth = { month: string; speedMS: number; count: number }

export type AerobicTrend = {
  // The usual heart rate of the steady runs, rounded to 5; speeds are scaled to it
  referenceHr: number
  months: AerobicMonth[]
}

// Only runs within this many beats of the reference count, so the scaling stays a small correction
export const heartRateBand = 10
const minimumRuns = 3

const median = (values: number[]) => {
  const sorted = [...values].sort((a, b) => a - b)
  const middle = Math.floor(sorted.length / 2)
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2
}

// Speed at the same heart rate per month: each run's speed is scaled by reference / own heart rate
// (efficiency, speed per beat, is close to constant within a narrow band), and the month gets the median.
export function aerobicTrend(points: AerobicPoint[]): AerobicTrend | null {
  if (points.length < minimumRuns) return null
  const referenceHr = Math.round(median(points.map((p) => p.hr)) / 5) * 5
  const near = points.filter((p) => Math.abs(p.hr - referenceHr) <= heartRateBand)
  if (near.length < minimumRuns) return null
  const byMonth = new Map<string, number[]>()
  for (const p of near) {
    const month = p.day.slice(0, 7)
    byMonth.set(month, [...(byMonth.get(month) ?? []), (p.speedMS * referenceHr) / p.hr])
  }
  const months = [...byMonth.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([month, speeds]) => ({ month, speedMS: median(speeds), count: speeds.length }))
  return { referenceHr, months }
}

// Seconds per km at the reference heart rate in the first and last month, if they differ
export function aerobicChange(trend: AerobicTrend) {
  if (trend.months.length < 2) return null
  const first = trend.months[0]
  const last = trend.months[trend.months.length - 1]
  return {
    from: 1000 / first.speedMS,
    to: 1000 / last.speedMS,
    fromMonth: first.month,
    toMonth: last.month,
  }
}
